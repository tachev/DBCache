using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Geo.Data
{
	public class DBCache<T> : IDatabase<T>, IDisposable where T: class, IDocument, new()
	{
		public const int MinimumSecondsBetweenCleaningTheCache = 30;
		public static int CacheItemTimeToLive;

		//TODO: Implement a setter which is going to save and empty the cache on disable. Turn off the RunAsync;
		private static bool DisableCache = false;

		private ConcurrentDictionary<string, CacheItem<T>> dbCache = new ConcurrentDictionary<string, CacheItem<T>>();
		private ConcurrentDictionary<string, string> saveQueue = new ConcurrentDictionary<string, string>();

		private IDatabase<T> _database;
		private IUpsertHandler<T> _upsertHandler;

		public DBCache(IDatabase<T> database)
		{
			_database = database;
			_upsertHandler = new UpsertHandlerBypass<T>();
			if (!DisableCache)
			{
				Task.Factory.StartNew(async () => { await RunAsync(CancellationToken.None); }, TaskCreationOptions.LongRunning);
			}
		}

		public DBCache(IDatabase<T> database, IUpsertHandler<T> upsertHandler)
		{
			_database = database;
			_upsertHandler = upsertHandler;
		}

		public async Task<string> CreateItemAsync(T item)
		{
			if (DisableCache)
			{
				return await _database.CreateItemAsync(item);
			}
			else
			{
				var updatedCacheItem = await CreateNewCacheItemAsync(item);
				dbCache.AddOrUpdate(item.Id, updatedCacheItem, (key, value) => value = updatedCacheItem);

				return item.Id;
			}
		}

		public async Task DeleteItemAsync(string id)
		{
			if (!DisableCache)
			{
				await CleanItemFromCacheAsync(id);
			}
			await _database.DeleteItemAsync(id);
		}

		public async Task<T> ReadItemByIdAsync(string itemId)
		{
			if (DisableCache)
			{
				return await _database.ReadItemByIdAsync(itemId);
			}
			else
			{
				return await ReadItemFromTheCache(itemId);
			}
		}

		private async Task<T> ReadItemFromTheCache(string itemId)
		{
			if (dbCache.TryGetValue(itemId, out CacheItem<T> cacheItem))
			{
				Trace.WriteLine($"Cache hit {itemId}");
				return cacheItem.Data;
			}
			else
			{
				Trace.WriteLine($"Cache miss {itemId}");
				return await ReadItemFromDatabase(itemId);
			}
		}

		private async Task<T> ReadItemFromDatabase(string itemId)
		{
			var databaseItem = await _database.ReadItemByIdAsync(itemId);

			if (databaseItem == null)
			{
				return null;
			}
			
			var newItem = CreateUpdateCacheItem(databaseItem);
			dbCache.AddOrUpdate(newItem.Id, newItem, (key, value) => value = newItem);
			return databaseItem;
		}

		private async Task ReadAndUpdateItemFromDatabase(string itemId, T item)
		{
			var databaseItem = await _database.ReadItemByIdAsync(itemId);

			if (databaseItem == null)
			{
				databaseItem = item;
			}
			else
			{
				databaseItem = _upsertHandler.UpdateItem(databaseItem, item);
			}
			
			var newItem = CreateUpdateCacheItem(databaseItem);
			dbCache.AddOrUpdate(newItem.Id, newItem, (key, value) => value = newItem);
		}

		public async Task ReplaceItemAsync(T item)
		{
			if (DisableCache)
			{
				await _database.ReplaceItemAsync(item);
				return;
			}
			else
			{
				var updatedCacheItem = CreateUpdateCacheItem(item);
				dbCache.AddOrUpdate(item.Id, updatedCacheItem, (key, value) => value = updatedCacheItem);

				AddToSaveQueueAsync(item.Id);
			}
		}

		public async Task<List<T>> SearchAsync(string searchQuery, string orderBy)
		{
			//REVIEW: [Improvement] We always read from the database, as we don't know if we have all the items in the cache. We can cache whole search result and while we have all the items we can return from result from the cache

			if (!DisableCache)
			{
				//We need to ensure that everything is saved before we read the items
				//REVIEW: [Improvement] If we need eventual consistency we can skip this 
				await SaveAllToDatabaseAsync();
			}

			//REVIEW: [Improvement] If we need higher level of consistency: Stop/lock the saving, to ensure we have the latest
			var documents = await _database.SearchAsync(searchQuery, orderBy);

			return EnsureLatest(documents);
		}

		private List<T> EnsureLatest(List<T> documents)
		{
			List<T> latestDocuments = new List<T>();
			foreach (var document in documents)
			{
				if (!DisableCache)
				{
					//We do that in case there is some unsaved data in the cache
					if (dbCache.TryGetValue(document.Id, out var cacheItem))
					{
						latestDocuments.Add(cacheItem.Data);
					}
					else
					{
						latestDocuments.Add(document);
					}
				}
				else
				{
					latestDocuments.Add(document);
				}
			}

			return latestDocuments;
		}

		public async Task<string> UpsertItemAsync(T item)
		{
			string itemId;

			if (DisableCache)
			{
				if (item.Id == null)
				{
					itemId = await _database.CreateItemAsync(item);
				}
				else
				{
					itemId = item.Id;
					var databaseItem = await _database.ReadItemByIdAsync(itemId);

					if (databaseItem == null)
					{
						databaseItem = item;
					}
					else
					{
						databaseItem = _upsertHandler.UpdateItem(databaseItem, item);
					}
					await _database.UpsertItemAsync(databaseItem);
				}

				return itemId;
			}
			else
			{

				if (item.Id == null)
				{
					var newItem = await CreateNewCacheItemAsync(item);
					itemId = newItem.Id;
					dbCache.AddOrUpdate(itemId, newItem, (key, value) => value = newItem);
				}
				else
				{
					itemId = item.Id;
					if (dbCache.TryGetValue(itemId, out var oldItem))
					{
						var updatedItem = _upsertHandler.UpdateItem(oldItem.Data, item);
						var updatedCacheItem = CreateUpdateCacheItem(updatedItem);
						dbCache.AddOrUpdate(itemId, updatedCacheItem, (key, value) => value = updatedCacheItem);
					}
					else
					{
						await ReadAndUpdateItemFromDatabase(itemId, item);
						//If it's not in the cache we'll check the database to get an item in the cache, because the new value can be only a partial update
					}

					AddToSaveQueueAsync(itemId);
				}

				return itemId;
			}
		}

		private async Task SaveToDatabaseAsync(string itemId)
		{
			if (dbCache.TryGetValue(itemId, out var item))
			{
				//REVIEW: Should we use replace here?
				await _database.UpsertItemAsync(item.Data);

				Trace.WriteLine($"Cache item saved to database {itemId}");
			}
		}
		
		protected async Task RunAsync(CancellationToken cancellationToken)
		{
			if (DisableCache)
			{
				return;
			}
			else
			{
				var lastCleaningOfTheCache = DateTime.Now;

				while (true)
				{
					await SaveAllToDatabaseAsync();
					if (cancellationToken.IsCancellationRequested)
					{
						return;
					}
					if (DisableCache)
					{
						return;
					}

					var timeFromTheLastCleaning = (DateTime.Now - lastCleaningOfTheCache).TotalSeconds;

					if (timeFromTheLastCleaning > MinimumSecondsBetweenCleaningTheCache)
					{
						await CleanTheCacheAsync();

						lastCleaningOfTheCache = DateTime.Now;
					}

					Thread.Sleep(TimeSpan.FromSeconds(1));
				}
			}
		}

		private async Task SaveAllToDatabaseAsync()
		{
			var ids = saveQueue.Keys.ToList();
			saveQueue.Clear();

			var tasks = new Task[ids.Count];
			for (int i = 0; i < ids.Count; i++)
			{
				tasks[i] = SaveToDatabaseAsync(ids[i]);
			}
			await Task.WhenAll(tasks);
		}

		private async Task CleanTheCacheAsync()
		{
			List<string> keys = dbCache.Keys.ToList();
			if (keys.Count > 0)
			{
				Trace.WriteLine("Cache: Attempting to clean {keys.Count} items ");
			}
			var currentTime = DateTime.Now;
			foreach (var key in keys)
			{
				if (dbCache[key].ExpirationTime < currentTime)
				{
					await CleanItemFromCacheAsync(key);
				}
			}
		}

		private async Task CleanItemFromCacheAsync(string itemId)
		{
			if (saveQueue.TryRemove(itemId, out string value))
			{
				await SaveToDatabaseAsync(itemId);
			}

			if (dbCache.TryRemove(itemId, out var item))
			{
				Trace.WriteLine($"Cache item removed: {itemId}");
			}	
		}
		
		private void AddToSaveQueueAsync(string itemId)
		{
			if (!saveQueue.ContainsKey(itemId))
			{
				saveQueue.TryAdd(itemId, itemId);
			}
		}
		
		private async Task<CacheItem<T>> CreateNewCacheItemAsync(T item)
		{
			var itemId = await _database.CreateItemAsync(item);
			item.Id = itemId;
			var result = new CacheItem<T>(item);
			return result;
		}
		
		private CacheItem<T> CreateUpdateCacheItem(T item)
		{
			var result = new CacheItem<T>(item);
			return result;
		}

		public Task<T> ReadItemAsync(Expression<Func<T, bool>> predicate)
		{
			//TODO:
			throw new NotImplementedException();
		}

		#region IDisposable Support
		private bool disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					SaveAllToDatabaseAsync().Wait();
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				//Set large fields to null.
				dbCache = null;

				disposedValue = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~DBCache() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion
	}
}
