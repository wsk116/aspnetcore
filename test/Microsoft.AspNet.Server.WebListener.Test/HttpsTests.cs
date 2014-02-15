﻿// -----------------------------------------------------------------------
// <copyright file="HttpsTests.cs" company="Microsoft">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.FeatureModel;
using Microsoft.AspNet.HttpFeature;
using Microsoft.AspNet.PipelineCore;
using Xunit;

namespace Microsoft.AspNet.Server.WebListener.Tests
{
    using AppFunc = Func<object, Task>;

    public class HttpsTests
    {
        private const string Address = "https://localhost:9090/";

        [Fact]
        public async Task Https_200OK_Success()
        {
            using (CreateServer(env =>
            {
                return Task.FromResult(0);
            }))
            {
                string response = await SendRequestAsync(Address);
                Assert.Equal(string.Empty, response);
            }
        }

        [Fact]
        public async Task Https_SendHelloWorld_Success()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                byte[] body = Encoding.UTF8.GetBytes("Hello World");
                httpContext.Response.ContentLength = body.Length;
                return httpContext.Response.Body.WriteAsync(body, 0, body.Length);
            }))
            {
                string response = await SendRequestAsync(Address);
                Assert.Equal("Hello World", response);
            }
        }

        [Fact]
        public async Task Https_EchoHelloWorld_Success()
        {
            using (CreateServer(env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                string input = new StreamReader(httpContext.Request.Body).ReadToEnd();
                Assert.Equal("Hello World", input);
                byte[] body = Encoding.UTF8.GetBytes("Hello World");
                httpContext.Response.ContentLength = body.Length;
                httpContext.Response.Body.Write(body, 0, body.Length);
                return Task.FromResult(0);
            }))
            {
                string response = await SendRequestAsync(Address, "Hello World");
                Assert.Equal("Hello World", response);
            }
        }

        [Fact]
        public async Task Https_ClientCertNotSent_ClientCertNotPresent()
        {
            using (CreateServer(async env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var tls = httpContext.GetFeature<IHttpTransportLayerSecurity>();
                Assert.NotNull(tls);
                await tls.LoadAsync();
                Assert.Null(tls.ClientCertificate);
            }))
            {
                string response = await SendRequestAsync(Address);
                Assert.Equal(string.Empty, response);
            }
        }

        [Fact]
        public async Task Https_ClientCertRequested_ClientCertPresent()
        {
            using (CreateServer(async env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var tls = httpContext.GetFeature<IHttpTransportLayerSecurity>();
                Assert.NotNull(tls);
                await tls.LoadAsync();
                Assert.NotNull(tls.ClientCertificate);
            }))
            {
                X509Certificate2 cert = FindClientCert();
                Assert.NotNull(cert);
                string response = await SendRequestAsync(Address, cert);
                Assert.Equal(string.Empty, response);
            }
        }

        private IDisposable CreateServer(AppFunc app)
        {
            IDictionary<string, object> properties = new Dictionary<string, object>();
            IList<IDictionary<string, object>> addresses = new List<IDictionary<string, object>>();
            properties["host.Addresses"] = addresses;

            IDictionary<string, object> address = new Dictionary<string, object>();
            addresses.Add(address);

            address["scheme"] = "https";
            address["host"] = "localhost";
            address["port"] = "9090";
            address["path"] = string.Empty;

            return OwinServerFactory.Create(app, properties);
        }

        private async Task<string> SendRequestAsync(string uri, 
            X509Certificate cert = null)
        {
            WebRequestHandler handler = new WebRequestHandler();
            handler.ServerCertificateValidationCallback = (a, b, c, d) => true;
            if (cert != null)
            {
                handler.ClientCertificates.Add(cert);
            }
            using (HttpClient client = new HttpClient(handler))
            {
                return await client.GetStringAsync(uri);
            }
        }

        private async Task<string> SendRequestAsync(string uri, string upload)
        {
            WebRequestHandler handler = new WebRequestHandler();
            handler.ServerCertificateValidationCallback = (a, b, c, d) => true;
            using (HttpClient client = new HttpClient(handler))
            {
                HttpResponseMessage response = await client.PostAsync(uri, new StringContent(upload));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }

        private X509Certificate2 FindClientCert()
        {
            var store = new X509Store();
            store.Open(OpenFlags.ReadOnly);

            foreach (var cert in store.Certificates)
            {
                bool isClientAuth = false;
                bool isSmartCard = false;
                foreach (var extension in cert.Extensions)
                {
                    var eku = extension as X509EnhancedKeyUsageExtension;
                    if (eku != null)
                    {
                        foreach (var oid in eku.EnhancedKeyUsages)
                        {
                            if (oid.FriendlyName == "Client Authentication")
                            {
                                isClientAuth = true;
                            }
                            else if (oid.FriendlyName == "Smart Card Logon")
                            {
                                isSmartCard = true;
                                break;
                            }
                        }
                    }
                }

                if (isClientAuth && !isSmartCard)
                {
                    return cert;
                }
            }
            return null;
        }
    }
}
