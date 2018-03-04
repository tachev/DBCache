using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Geo.Data.DBCache.Tests
{
	public class TestDatabase : IDatabase<Document>
	{
		public int SimulateDatabaseDelay = 20;

		ConcurrentDictionary<string, Document> store = new ConcurrentDictionary<string, Document>();

		public Task<string> CreateItemAsync(Document item)
		{
			return Task.Run(() =>
			{
				store.AddOrUpdate(item.Id, item, (key, value) => value = item);
				Delay();
				return item.Id;
			});
		}

		public Task<Document> ReadItemByIdAsync(string id)
		{
			return Task.Run(() =>
			{
				Delay();
				if (store.ContainsKey(id))
				{
					return store[id];
				}
				else
				{
					return null;
				}
			});
		}

		private void Delay()
		{
			if (SimulateDatabaseDelay > 0)
			{
				Thread.Sleep(SimulateDatabaseDelay);
			}
		}

		public Task<string> UpsertItemAsync(Document item)
		{
			return Task.Run(() =>
			{
				store.AddOrUpdate(item.Id, item, (key, value) => value = item);
				Delay();
				return item.Id;
			});
		}

		public Task DeleteItemAsync(string id)
		{
			return Task.Run(() =>
			{
				store.TryRemove(id, out Document item);
				Delay();
			});
		}

		public Task<List<Document>> SearchAsync(string query, string orderBy)
		{
			return Task.Run(() =>
			{
				if (SimulateDatabaseDelay > 0)
				{
					Thread.Sleep(SimulateDatabaseDelay * store.Count);
				}
				return store.Values.ToList();
			});
		}

		public Task<Document> ReadItemAsync(Expression<Func<Document, bool>> predicate)
		{
			throw new NotImplementedException();
		}
		
		public Task ReplaceItemAsync(Document item)
		{
			return Task.Run(() =>
			{
				store.AddOrUpdate(item.Id, item, (key, value) => value = item);
				Delay();
				return item.Id;
			});
		}
	}
}
