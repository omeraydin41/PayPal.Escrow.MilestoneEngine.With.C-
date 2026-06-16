using NLog;
using NLog.Web;
using PayPal.Escrow.MilestoneEngine.With.C_.Configurations;
using PayPal.Escrow.MilestoneEngine.With.C_.Services;

// 1. NLog ilk kurulumunu try bloðu dýþýnda yapýyoruz
var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
logger.Debug("Uygulama baþlatýlýyor (NLog aktif)...");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // 2. NLog'u varsayýlan loglama saðlayýcýsý olarak sisteme entegre ediyoruz
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // 3. Configuration & Options Kaydý
    builder.Services.Configure<PaypalSettings>(builder.Configuration.GetSection("PaypalSettings"));

    // 4. Dependency Injection (DI) Kayýtlarý
    builder.Services.AddSingleton<IContractRepository, ContractRepository>();
    builder.Services.AddScoped<IPaypalService, PaypalService>();

    // 5. Controllers ve Swagger Kayýtlarý
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // 6. Swagger Middleware Ayarlarý (Geliþtirme Ortamý Ýçin)
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "B2B Escrow API V1");
            options.RoutePrefix = string.Empty; // Uygulama açýlýnca direkt Swagger ana sayfaya gelsin diye
        });
    }

    // 7. HTTP Pipeline ve Middleware Daðýtýmý
    app.UseHttpsRedirection();
    app.UseAuthorization();
    app.MapControllers();

    // 8. Uygulamayý Baþlat
    app.Run();
}
catch (Exception exception)
{
    // Uygulama ayaða kalkarken (örn: nlog.config eksikliði veya port çakýþmasý) bir hata oluþursa yakala
    logger.Error(exception, "Uygulama baþlatýlýrken kritik hata oluþtu!");
    throw;
}
finally
{
    // Uygulama kapandýðýnda veya durdurulduðunda NLog belleðini temizle
    LogManager.Shutdown();
}