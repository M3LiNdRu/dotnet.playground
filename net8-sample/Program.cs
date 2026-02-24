var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var handler = () => "Hello World!";

app.MapGet("/", handler);

GetInfoEndpoint.Map(app);

app.Run();
