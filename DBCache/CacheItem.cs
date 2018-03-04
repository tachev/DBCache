using System;

namespace Geo.Data
{
	internal class CacheItem<T> where T: class, IDocument, new()
	{
		public DateTime ExpirationTime;

		public string Id;

		public T Data;

		public CacheItem()
		{
		}

		public CacheItem(T item)
		{
			var currentTime = DateTime.Now;
			Id = item.Id;
			ExpirationTime = DateTime.Now.AddSeconds(DBCache<T>.CacheItemTimeToLive);
			Data = item;
		}
	}
}
