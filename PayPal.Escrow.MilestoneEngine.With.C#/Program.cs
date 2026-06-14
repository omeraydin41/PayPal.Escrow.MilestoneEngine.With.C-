using Microsoft.AspNetCore.Builder;
using PayPal.Escrow.MilestoneEngine.With.C_.Configurations;
using PayPal.Escrow.MilestoneEngine.With.C_.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<PaypalSettings>(
    builder.Configuration.GetSection("PaypalSettings"));

// Dependency Injection
builder.Services.AddSingleton<IContractRepository, ContractRepository>();
builder.Services.AddScoped<IPaypalService, PaypalService>();

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "B2B Escrow API V1");
        options.RoutePrefix = string.Empty; // Uygulama açýlýnca direkt Swagger gelsin
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();