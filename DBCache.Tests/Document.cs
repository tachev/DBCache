using Geo.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geo.Data.DBCache.Tests
{
	public class Document : IDocument
	{
		private Dictionary<string, object> _content;
		private const string TestDocumentType = "Test";

		public Document()
		{
			_content = new Dictionary<string, object>
			{
				["id"] = Guid.NewGuid().ToString(),
				["documentType"] = TestDocumentType
			};
		}

		public string Id {
			get
			{
				return (string)_content["id"];
			}
			set
			{
				_content["id"] = value;
			}
		}

		public string DocumentType
		{
			get
			{
				return (string)_content["documentType"];
			}
		}

		public Dictionary<string, object> Content { get => _content; set => _content = value; }
	}
}
