namespace StudyMarket.Tests.Integration;

public class OpenApiTests(ApiFactory api)
{
    [Fact]
    public async Task OpenApi_document_describes_endpoints_and_jwt()
    {
        var document = await api.CreateClient().GetStringAsync("/openapi/v1.json");

        Assert.Contains("\"/api/courses\"", document);
        Assert.Contains("\"bearerFormat\": \"JWT\"", document);
    }
}
