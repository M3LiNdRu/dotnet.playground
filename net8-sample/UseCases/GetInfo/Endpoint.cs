public static class GetInfoEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/info", Handler.GetInfo);
    }
}