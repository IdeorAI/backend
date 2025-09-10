using IdeorAI.Client;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

// chave ainda está hardcoded no seu Program.cs atual :contentReference[oaicite:6]{index=6}
// Program.cs
var apiKey = builder.Configuration["Gemini:ApiKey"];
builder.Services.AddSingleton(new GeminiApiClient(apiKey));


// CORS
const string FrontendCors = "FrontendCors";
builder.Services.AddCors(opt =>
{
    opt.AddPolicy(FrontendCors, p =>
    {
        p.WithOrigins("http://localhost:3000", "https://front-end-plum-nu.vercel.app") // ajuste!
         .AllowAnyMethod()
         .AllowAnyHeader()
         .WithExposedHeaders("Content-Disposition");
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
var app = builder.Build();
app.UseCors(option => option
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Content-Dispostion"));
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();