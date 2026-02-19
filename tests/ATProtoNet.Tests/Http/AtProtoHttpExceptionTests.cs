using System.Net;
using ATProtoNet.Http;

namespace ATProtoNet.Tests.Http;

public class AtProtoHttpExceptionTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var ex = new AtProtoHttpException("InvalidRequest", "Bad input", HttpStatusCode.BadRequest, "{\"error\":\"InvalidRequest\"}");

        Assert.Equal("InvalidRequest", ex.ErrorType);
        Assert.Equal("Bad input", ex.ErrorMessage);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("{\"error\":\"InvalidRequest\"}", ex.ResponseBody);
        Assert.Contains("InvalidRequest", ex.Message);
        Assert.Contains("Bad input", ex.Message);
    }

    [Fact]
    public void Constructor_SimpleMessage()
    {
        var ex = new AtProtoHttpException("Something went wrong", HttpStatusCode.InternalServerError);

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Equal("Something went wrong", ex.Message);
        Assert.Null(ex.ErrorType);
        Assert.Null(ex.ErrorMessage);
    }

    [Fact]
    public void IsHttpRequestException()
    {
        var ex = new AtProtoHttpException("Test", "msg", HttpStatusCode.Forbidden);

        Assert.IsAssignableFrom<HttpRequestException>(ex);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void StatusCode_PreservesValue(HttpStatusCode statusCode)
    {
        var ex = new AtProtoHttpException("Test", "msg", statusCode);
        Assert.Equal(statusCode, ex.StatusCode);
    }
}
