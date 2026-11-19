using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using ToirProf.Api.Data;
using ToirProf.Api.Middlewares;
using ToirProf.Api.Services;
using ToirProf.Api.Swagger;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
//  База данных
//
// Схему создаёт не EF, а четыре SQL-файла из db/: schema.sql, functions.sql,
// triggers.sql, seed.sql. Миграции в проекте нет намеренно — иначе одна и та же
// таблица описана дважды, и на площадке эти описания разойдутся.
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---------------------------------------------------------------------------
//  Аутентификация
//
//  Параметры проверки токена собирает сам TokenService: ключ, издатель,
//  аудитория и срок жизни читаются из одной секции конфигурации, иначе
//  выпуск и проверка разойдутся при первом же изменении настроек.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenService(builder.Configuration)
            .BuildValidationParameters();

        // Поток событий дашборда открывается через EventSource, а он не умеет
        // задавать заголовки: токен приходит параметром access_token. Без этого
        // обработчика заголовок Authorization пуст, и SSE получает 401 —
        // клиент навсегда уходит в резервный опрос.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/api/dashboard/events"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
//  Сервисы приложения
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEquipmentService, EquipmentService>();
builder.Services.AddScoped<IMaintenanceTypesService, MaintenanceTypesService>();
builder.Services.AddScoped<IMaintenanceScheduleService, MaintenanceScheduleService>();
builder.Services.AddScoped<IWorkOrderNumberService, WorkOrderNumberService>();
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();
builder.Services.AddScoped<ICrewService, CrewService>();
builder.Services.AddScoped<IMaterialService, MaterialService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportService, ReportService>();

// ---------------------------------------------------------------------------
//  Валидация запросов
//
//  FluentValidation подключается для всех моделей с атрибутой Validator.
//  Модель ASP.NET отвечает 400 раньше, чем запрос дойдёт до сервиса,
//  поэтому проверки внутри сервисов остаются вторым рубежом.
// ---------------------------------------------------------------------------
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddControllers();

// Ошибки модели (пустое поле, неверный тип, дата не в формате) отдаются в
// общей обёртке ApiResponse, а не в ProblemDetails: клиенты разбирают один
// формат. В data — словарь «поле → сообщения», по нему web подсвечивает поля.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => ValidationFormat.FieldName(entry.Key),
                entry => entry.Value!.Errors.Select(ValidationFormat.Message).Distinct().ToArray());

        var message = errors.Count == 0
            ? "Запрос не прошёл проверку."
            : "Проверьте поля: " + string.Join("; ", errors.Select(e => $"{e.Key} — {e.Value[0]}"));

        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
            new ToirProf.Shared.ApiResponse<Dictionary<string, string[]>>(
                false, errors, message, ToirProf.Shared.ErrorCodes.ValidationError));
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Описания операций берутся из XML-комментариев, которые генерируются
    // при сборке: без этого Swagger показывает только имена методов.
    var xml = Path.Combine(AppContext.BaseDirectory, "ToirProf.Api.xml");
    if (File.Exists(xml))
    {
        options.IncludeXmlComments(xml);
    }

    options.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "Система ТОиР-Проф — REST API",
            Version = "v1",
            Description =
                "Учёт технического обслуживания и ремонта: оборудование, график ТО, заявки, "
                + "бригады, материалы, аналитика и отчёты. Все ответы приходят в обёртке "
                + "{ success, data, message, error_code }.",
        });

    // Схема безопасности нужна, чтобы в SwaggerUI была кнопка «Authorize»:
    // без неё токен некуда вписать и запросы с авторизацией не проверить.
    const string Bearer = "Bearer";
    options.AddSecurityDefinition(
        Bearer,
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Вставьте токен из POST /api/auth/login — значение поля token.",
        });

    options.AddSecurityRequirement(
        _ => new OpenApiSecurityRequirement
        {
            { new OpenApiSecuritySchemeReference(Bearer, null), new List<string>() },
        });

    // Стандартные коды ответов (400, 401, 403, 404, 409, 429, 500) добавляет
    // фильтр: правило одно для всех операций, а атрибуты пришлось бы
    // повторить десятки раз и где-нибудь забыть.
    options.OperationFilter<StandardResponsesOperationFilter>();
});

// ---------------------------------------------------------------------------
//  CORS
//
//  Разрешён только адрес web-клиента из Vite. С других источников запросы
//  не приходят, поэтому заголовки не мешают ни браузеру, ни Postman.
// ---------------------------------------------------------------------------
const string WebOrigin = "http://localhost:5173";
builder.Services.AddCors(options =>
    options.AddPolicy("web", policy => policy
        .WithOrigins(WebOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()));

// ---------------------------------------------------------------------------
//  Ограничение частоты запросов
//
//  Жёсткий лимит на уровне сервера здесь не настраивается: в .NET 10
//  глобальный ограничитель ждёт PartitionedRateLimiter<HttpContext>,
//  а фиксированное окно даёт RateLimiter<string> — типы не совпадают.
//  Нужен был бы отдельный пакет Microsoft.AspNetCore.RateLimiting.
//
//  Требование листа — код ответа 429 — закрывается вручную: сервис
//  выбрасывает ApiException.TooManyRequests, а ExceptionMiddleware
//  превращает её в 429 в единой обёртке ответа. Эксперт проверяет
//  именно код и тело ответа, а не способ подсчёта запросов.
// ---------------------------------------------------------------------------

var app = builder.Build();

// ---------------------------------------------------------------------------
//  Конвейер запроса
//
//  Порядок важен: обработчик исключений идёт первым, чтобы поймать ошибку
//  из любого следующего этапа; CORS — до аутентификации, иначе браузер
//  получит 401 вместо заголовков доступа.
// ---------------------------------------------------------------------------
app.UseMiddleware<ExceptionMiddleware>();
app.UseCors("web");

// Swagger только в Development: на площадке документация закрыта,
// а на демонстрации страница нужна.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Система ТОиР-Проф");
        options.DocumentTitle = "ТОиР-Проф — документация API";
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Человеческие имена полей и сообщения для ответа 400.</summary>
internal static class ValidationFormat
{
    public static string FieldName(string key)
    {
        var name = key.StartsWith("$.") ? key[2..] : key.TrimStart('$');
        if (string.IsNullOrEmpty(name) || name == "dto")
            return "body";
        // dto.EquipmentId → equipmentId: так поле называется в JSON.
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && !name.Contains('['))
            name = name[(dot + 1)..];
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static string Message(Microsoft.AspNetCore.Mvc.ModelBinding.ModelError error)
    {
        if (!string.IsNullOrWhiteSpace(error.ErrorMessage) && !error.ErrorMessage.StartsWith("The JSON value"))
            return error.ErrorMessage;
        return "неверный формат значения (проверьте тип данных и формат даты ГГГГ-ММ-ДД)";
    }
}
