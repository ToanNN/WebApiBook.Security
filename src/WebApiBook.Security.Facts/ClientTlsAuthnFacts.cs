﻿using System.Collections.Generic;
using System.IdentityModel.Selectors;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Hosting;
using System.Web.Http.SelfHost;
using WebApiBook.Security.Common;
using Xunit;

namespace WebApiBook.Security.Facts
{
    public class HelloController : ApiController
    {
        public HttpResponseMessage Get()
        {
            //var clientCert = Request.GetClientCertificate();
            var clientCert = Request.GetRequestContext().ClientCertificate;
            var clientName = clientCert == null ? "stranger" : clientCert.Subject;
            return new HttpResponseMessage
            {
                Content = new StringContent("Hello there, " + clientName)
            };
        }
    }

    public class ClientTlsAuthnFacts
    {
        [Fact]
        public async Task Actions_can_get_the_client_certificate()
        {
            var config = new HttpSelfHostConfiguration("https://www.example.net:8443");
            config.ClientCredentialType = HttpClientCredentialType.Certificate;
            config.Routes.MapHttpRoute(
                "ApiDefault",
                "{controller}/{id}",
                new {id=RouteParameter.Optional }
                );
            var server = new HttpSelfHostServer(config);
            server.OpenAsync().Wait();
            using (var client = new HttpClient(new HttpClientHandler() { ClientCertificateOptions = ClientCertificateOption.Automatic }))
            {
                var resp = await client.GetAsync("https://www.example.net:8443/hello");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var body = await resp.Content.ReadAsStringAsync();
                Assert.False(body.Contains("stranger"));
            }
        }

        [Fact]
        public async Task SomeFact()
        {
            var config = new HttpSelfHostConfiguration("https://www.example.net:8443");
            const string x509AuthnMethod = "http://schemas.microsoft.com/ws/2008/06/identity/authenticationmethod/x509";
            config.MessageHandlers.Add(new X509CertificateMessageHandler(
                X509CertificateValidator.None,
                IssuerMapper.FromIssuerRegistry(new SimpleIssuerNameRegistry())
                ));
            config.MessageHandlers.Add(new
                FuncBasedDelegatingHandler((req, cont) =>
                {
                    const string issuer = "CN=Demo Certification Authority, O=Web API Book";
                    var principal = Thread.CurrentPrincipal as ClaimsPrincipal;
                    if (principal == null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                    var identity =
                        principal.Identities.FirstOrDefault(id => id.AuthenticationType == x509AuthnMethod);
                    if (identity == null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                    return Task.FromResult(
                        new HttpResponseMessage(
                            identity.Claims.Any(
                                claim => claim.Type == ClaimTypes.Email
                                    && claim.Value == "bob@webapibook.net"
                                    && claim.Issuer == issuer)
                                ? HttpStatusCode.OK
                                : HttpStatusCode.Unauthorized));
                }));

            config.ClientCredentialType = HttpClientCredentialType.Certificate;
            var server = new HttpSelfHostServer(config);
            server.OpenAsync().Wait();
            using (var client = new HttpClient(new HttpClientHandler() { ClientCertificateOptions = ClientCertificateOption.Automatic }))
            {
                var resp = await client.GetAsync("https://www.example.net:8443");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }
        }
    }
}
