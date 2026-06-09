using PayPal.Escrow.MilestoneEngine.With.C_.Configurations;
using PayPal.Escrow.MilestoneEngine.With.C_.Services;

var builder = WebApplication.CreateBuilder(args);

// appsettings'den verileri oku ve DI sistemine entegre et
builder.Services.Configure<PaypalSettings>(builder.Configuration.GetSection("PaypalSettings"));
// Veri deposunu DI sistemine kaydet
builder.Services.AddSingleton<IContractRepository, ContractRepository>();

// Servisimizi baðýmlýlýk konteynerýna kaydet
builder.Services.AddScoped<IPaypalService, PaypalService>();
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
