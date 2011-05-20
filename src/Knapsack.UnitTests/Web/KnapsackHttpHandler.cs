﻿using System;
using System.Collections.Specialized;
using System.IO;
using System.IO.IsolatedStorage;
using System.Web;
using Knapsack.CoffeeScript;
using Moq;
using Should;
using Xunit;
using Knapsack.Utilities;

namespace Knapsack.Web
{
    // Warning: These are not strictly "unit" tests given the real file system access!
    // However, they still run fast and creating a whole separate project for them seems
    // like overkill at the moment.

    public class KnapsackHttpHandler_tests : IDisposable
    {
        KnapsackHttpHandler handler;
        Mock<HttpContextBase> httpContext;
        Mock<HttpRequestBase> httpRequest;
        Mock<HttpResponseBase> httpResponse;
        IsolatedStorageFile storage;
        NameValueCollection requestHeaders;
        Mock<HttpCachePolicyBase> cache;
        Stream responseOutputStream;
        ModuleContainer scriptModuleContainer;
        ModuleContainer stylesheetModuleContainer;
        private Mock<HttpServerUtilityBase> server;

        public KnapsackHttpHandler_tests()
        {
            var coffeeScriptCompiler = new CoffeeScriptCompiler(File.ReadAllText);
            storage = IsolatedStorageFile.GetUserStoreForAssembly();
            rootDirectory = Path.GetFullPath(Guid.NewGuid().ToString());

            // Create a fake set of scripts in modules.
            Directory.CreateDirectory(Path.Combine(rootDirectory, "lib"));
            File.WriteAllText(Path.Combine(rootDirectory, "lib", "jquery.js"),
                "function jQuery(){}");
            File.WriteAllText(Path.Combine(rootDirectory, "lib", "knockout.js"),
                "function knockout(){}");

            Directory.CreateDirectory(Path.Combine(rootDirectory, "app"));
            File.WriteAllText(Path.Combine(rootDirectory, "app", "widgets.js"),
                "/// <reference path=\"../lib/jquery.js\"/>\r\n/// <reference path=\"../lib/knockout.js\"/>\r\nfunction widgets(){}");
            File.WriteAllText(Path.Combine(rootDirectory, "app", "main.js"),
                "/// <reference path=\"widgets.js\"/>\r\nfunction main() {}");
            File.WriteAllText(Path.Combine(rootDirectory, "app", "test.coffee"),
                "x = 1");

            var builder = new ScriptModuleContainerBuilder(storage, rootDirectory, coffeeScriptCompiler);
            builder.AddModule("lib");
            builder.AddModule("app");
            scriptModuleContainer = builder.Build();
            scriptModuleContainer.UpdateStorage("scripts.xml");

            var styleBuilder = new StylesheetModuleContainerBuilder(storage, rootDirectory, "/");

            handler = new KnapsackHttpHandler(() => scriptModuleContainer, () => stylesheetModuleContainer, coffeeScriptCompiler);

            httpContext = new Mock<HttpContextBase>();
            httpRequest = new Mock<HttpRequestBase>();
            requestHeaders = new NameValueCollection();
            httpResponse = new Mock<HttpResponseBase>();
            responseOutputStream = new MemoryStream();
            cache = new Mock<HttpCachePolicyBase>();
            server = new Mock<HttpServerUtilityBase>();

            httpRequest.ExpectGet(r => r.Headers).Returns(requestHeaders);
            httpResponse.ExpectGet(r => r.OutputStream).Returns(responseOutputStream);
            httpResponse.ExpectGet(r => r.Cache).Returns(cache.Object);
            httpContext.ExpectGet(c => c.Request).Returns(httpRequest.Object);
            httpContext.ExpectGet(c => c.Response).Returns(httpResponse.Object);
            httpContext.ExpectGet(c => c.Server).Returns(server.Object);

            httpResponse.Expect(r => r.Write(It.IsAny<string>())).Callback<string>(data =>
            {
                var writer = new StreamWriter(responseOutputStream);
                writer.Write(data);
                writer.Flush();
            });
        }

        [Fact]
        public void Module_request_returns_module_from_storage()
        {
            httpRequest.ExpectGet(r => r.PathInfo).Returns("/scripts/lib_123");
            handler.ProcessRequest(httpContext.Object);

            var output = GetOutputString();
            output.ShouldEqual("function jQuery(){}function knockout(){}");
        }

        [Fact]
        public void Module_request_sets_cache_headers()
        {
            var nextYear = DateTime.UtcNow.AddYears(1);
            httpRequest.ExpectGet(r => r.PathInfo).Returns("/scripts/lib_123");
            handler.ProcessRequest(httpContext.Object);

            cache.Verify(c => c.SetCacheability(HttpCacheability.Public));
            cache.Verify(c => c.SetETag(It.IsAny<string>()));
            cache.Verify(c => c.SetExpires(It.Is<DateTime>(d => d >= nextYear)));
        }

        [Fact]
        public void Request_missing_path_info_returns_BadRequest()
        {
            httpRequest.ExpectGet(r => r.PathInfo).Returns("");
            handler.ProcessRequest(httpContext.Object);
            httpResponse.ExpectSet(r => r.StatusCode, 400);
        }

        [Fact]
        public void Request_incomplete_path_info_returns_BadRequest()
        {
            httpRequest.ExpectGet(r => r.PathInfo).Returns("/");
            handler.ProcessRequest(httpContext.Object);
            httpResponse.ExpectSet(r => r.StatusCode, 400);
        }

        [Fact]
        public void Request_nonexistent_module_returns_NotFound()
        {
            httpRequest.ExpectGet(r => r.PathInfo).Returns("/modules/fail_123");
            handler.ProcessRequest(httpContext.Object);
            httpResponse.ExpectSet(r => r.StatusCode, 404);
        }

        [Fact]
        public void Request_module_without_hash_in_url_returns_OK()
        {
            // Knapsack won't generate URLs without the hash, but it's better to avoid
            // throwing an exception if we can help it!
            httpRequest.ExpectGet(r => r.PathInfo).Returns("/modules/lib");
            handler.ProcessRequest(httpContext.Object);
            httpResponse.ExpectSet(r => r.StatusCode, 200);
        }

        [Fact]
        public void Request_module_using_etag_returns_NotModified()
        {
            var etag = scriptModuleContainer.FindModule("app").Hash.ToHexString();
            requestHeaders["If-Modified-Since"] = etag;
            httpRequest.ExpectGet(r => r.PathInfo).Returns("/modules/lib_" + etag);
            handler.ProcessRequest(httpContext.Object);
            httpResponse.ExpectSet(r => r.StatusCode, 304);
        }

        [Fact]
        public void Coffee_request_compiles_coffee_file_and_returns_javascript()
        {
            httpRequest.ExpectGet(r => r.PathInfo).Returns("/coffee/app/test");
            server.Expect(s => s.MapPath("~/app/test.coffee")).Returns(rootDirectory + "/app/test.coffee");
            handler.ProcessRequest(httpContext.Object);

            httpResponse.VerifySet(r => r.ContentType, "text/javascript");
            GetOutputString().ShouldEqual("(function() {\n  var x;\n  x = 1;\n}).call(this);\n");
        }

        string GetOutputString()
        {
            responseOutputStream.Position = 0;
            using (var reader = new StreamReader(responseOutputStream))
            {
                return reader.ReadToEnd();
            }
        }

        public void Dispose()
        {
            Directory.Delete(rootDirectory, true);

            if (storage != null)
            {
                storage.Remove();
                storage.Dispose();
            }
        }


        public string rootDirectory { get; set; }
    }
}
