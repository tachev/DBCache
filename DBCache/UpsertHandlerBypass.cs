using System;
using System.Collections.Generic;
using System.Text;

namespace Geo.Data
{
    public class UpsertHandlerBypass<T> : IUpsertHandler<T> where T : class, IDocument, new()
	{
		public UpsertHandlerBypass()
		{
		}

		public T UpdateItem(T originalItem, T change)
		{
			return change;
		}
    }
}
