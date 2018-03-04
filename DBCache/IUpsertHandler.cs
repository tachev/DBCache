using System;
using System.Collections.Generic;
using System.Text;

namespace Geo.Data
{
    public interface IUpsertHandler<T> where T : class, IDocument, new()
	{
		T UpdateItem(T originalItem, T change);
    }
}
