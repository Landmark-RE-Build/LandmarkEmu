using LandmarkEmulator.WebAPI;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The web API's plain-text endpoints (/create, /authenticate, /delete) take the user name and password from the
    /// request body only (AuthWebAPI.ReadCredentials): a form or JSON. A password in the address is refused unread, since
    /// request addresses end up in logs.
    /// </summary>
    public class WebApiCredentialsTests
    {
        private static HttpContext Request(string query, string contentType, string body)
        {
            var http = new DefaultHttpContext();
            http.Request.Method = "POST";
            http.Request.QueryString = new QueryString(query);
            if (contentType != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                http.Request.ContentType = contentType;
                http.Request.ContentLength = bytes.Length;
                http.Request.Body = new MemoryStream(bytes);
            }
            return http;
        }

        private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

        [Fact]
        public async Task PasswordInTheAddress_IsRefused()
        {
            var (user, password, refusal) = await AuthWebAPI.ReadCredentials(
                Request("?username=alice&password=secret1", "application/x-www-form-urlencoded", "username=alice&password=secret1"));
            Assert.Equal(400, Status(refusal));
            Assert.Null(user);
            Assert.Null(password);
        }

        [Fact]
        public async Task FormBody_IsRead()
        {
            var (user, password, refusal) = await AuthWebAPI.ReadCredentials(
                Request("", "application/x-www-form-urlencoded", "username=alice&password=p%26ss%20word"));
            Assert.Null(refusal);
            Assert.Equal("alice", user);
            Assert.Equal("p&ss word", password);
        }

        [Fact]
        public async Task JsonBody_IsRead_AnyCase()
        {
            var (user, password, refusal) = await AuthWebAPI.ReadCredentials(
                Request("", "application/json", "{\"Username\":\"alice\",\"password\":\"secret1\"}"));
            Assert.Null(refusal);
            Assert.Equal("alice", user);
            Assert.Equal("secret1", password);
        }

        [Fact]
        public async Task NoBody_OrMalformedJson_IsABadRequest()
        {
            Assert.Equal(400, Status((await AuthWebAPI.ReadCredentials(Request("?username=alice", null, null))).Refusal));
            Assert.Equal(400, Status((await AuthWebAPI.ReadCredentials(Request("", "application/json", "{\"username\":"))).Refusal));
        }

        [Fact]
        public async Task OversizedBody_IsRefusedUnread()
            => Assert.Equal(413, Status((await AuthWebAPI.ReadCredentials(
                Request("", "application/x-www-form-urlencoded", "username=a&password=" + new string('x', 5000)))).Refusal));
    }
}
