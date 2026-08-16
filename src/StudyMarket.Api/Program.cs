using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using StudyMarket.Api.Data;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(config.GetConnectionString("Default")));

builder.Services.AddValidation();     // walidacja atrybutów [Required], [Range]... w Minimal API (.NET 10)
builder.Services.AddProblemDetails(); // błędy w standardowym formacie RFC 9457
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter())); // enumy jako tekst w JSON

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();               // /openapi/v1.json
app.MapScalarApiReference();    // /scalar - interaktywna dokumentacja


app.Run();
