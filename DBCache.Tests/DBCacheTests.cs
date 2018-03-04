using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Geo.Data.DBCache.Tests
{
	[TestClass]
	public class DBCacheTests
	{
		private const string TestDocumentType = "TestDocument";
		private IDatabase<Document> database;

		[TestInitialize]
		public void TestInitialize()
		{
			var testDatabase = new TestDatabase();
			//All tests should work fine without cache. The only difference should be the time for execution
			//database = testDatabase;
			database = new DBCache<Document>(testDatabase);
		}


		[TestCleanup]
		public async Task TestCleanupAsync()
		{
			var searchResults = await database.SearchAsync($"documentType eq '{TestDocumentType}'", null);
			foreach (var item in searchResults)
			{
				await database.DeleteItemAsync((string)item.Content["id"]);
			}
			if (database is IDisposable dbCache)
			{
				dbCache.Dispose();
			}
		}


		[TestMethod]
		public async Task AddReadItemToCache()
		{
			var document = new Document();
			document.Content["testData"] = "Test";
			document.Content["id"] = await database.CreateItemAsync(document);
			await AssertValue(document, "testData", "Test");

			document.Content["testData"] = "Test2";
			document.Content["updatedData"] = "New value";
			await database.UpsertItemAsync(document);
			var resultDocument1 = await AssertValue(document, "testData", "Test2");
			Assert.AreEqual("New value", resultDocument1.Content["updatedData"]);

			document.Content["testData"] = "Test3";
			document.Content.Remove("updatedData");
			await database.ReplaceItemAsync(document);
			var resultDocument2 = await AssertValue(document, "testData", "Test3");
			Assert.IsFalse(resultDocument2.Content.ContainsKey("updatedData"));
		}

		private async Task PrepareData()
		{
			var document = CreateDocument();
			document.Content["testData"] = "Test";
			await database.CreateItemAsync(document);
			await AssertValue(document, "testData", "Test");

			var document2 = CreateDocument();
			document2.Content["testData"] = "Test";
			await database.CreateItemAsync(document2);
			await AssertValue(document2, "testData", "Test");

			document.Content["testData"] = "Test2";
			document.Content["updatedData"] = "New value";
			await database.UpsertItemAsync(document);
			var resultDocument1 = await AssertValue(document, "testData", "Test2");
			Assert.AreEqual("New value", resultDocument1.Content["updatedData"]);

			document2.Content["testData"] = "Test2";
			document2.Content["updatedData"] = "New value";
			await database.UpsertItemAsync(document2);
			var resultDocument2 = await AssertValue(document2, "testData", "Test2");
			Assert.AreEqual("New value", resultDocument2.Content["updatedData"]);

		}

		[TestMethod]
		public async Task DeleteItem()
		{
			var document = CreateDocument();
			document.Content["testData"] = "Test";
			await database.CreateItemAsync(document);
			await AssertValue(document, "testData", "Test");

			await database.DeleteItemAsync(document.Id);
			var savedDocument = await database.ReadItemByIdAsync(document.Id);
			Assert.IsNull(savedDocument);
		}

		[TestMethod]
		public async Task ReadPerformance()
		{
			var startTime = DateTime.Now;
			var document = CreateDocument();
			document.Content["testData"] = "Test";
			await database.CreateItemAsync(document);
			for (int i = 0; i < 100; i++)
			{
				await AssertValue(document, "testData", "Test");
			}

			var timeSpan = DateTime.Now - startTime;

			Trace.WriteLine("Total seconds: " + timeSpan.TotalSeconds);
			//This may defer depends on what machine we are executing on, but if we are not hiting the cahse it should be between 10-20 seconds, as it's garanteed 20 ms per read from the database
			Assert.IsTrue(timeSpan.TotalSeconds < 1);

		}

		[TestMethod]
		public async Task UpdatePerformance()
		{
			var startTime = DateTime.Now;
			var document = CreateDocument();
			for (int i = 0; i < 100; i++)
			{
				document.Content["testData"] = "Test" + i;
				await database.UpsertItemAsync(document);
				await AssertValue(document, "testData", "Test" + i);
			}

			var timeSpan = DateTime.Now - startTime;

			Trace.WriteLine("Total seconds: " + timeSpan.TotalSeconds);

			//This may defer depends on what machine we are executing, but if we are not hiting the cahse it should be between 10-20 seconds, as it's garanteed 20 ms per read from the database
			Assert.IsTrue(timeSpan.TotalSeconds < 1);
		}

		private async Task<Document> AssertValue(Document document, string propertyName, string value)
		{
			var resultDocument = await database.ReadItemByIdAsync((string)document.Content["id"]);
			Assert.AreEqual(value, resultDocument.Content[propertyName]);
			return resultDocument;
		}

		private Document CreateDocument()
		{
			return new Document();
		}
	}
}
