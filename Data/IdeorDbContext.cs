using Microsoft.EntityFrameworkCore;
using IdeorAI.Model.Entities;

namespace IdeorAI.Data;

/// <summary>
/// DbContext principal do IdeorAI
/// Conecta-se ao PostgreSQL do Supabase e mapeia as tabelas existentes
/// </summary>
public class IdeorDbContext : DbContext
{
    public IdeorDbContext(DbContextOptions<IdeorDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Perfis de usuários
    /// </summary>
    public DbSet<Profile> Profiles { get; set; } = null!;

    /// <summary>
    /// Projetos de startup
    /// </summary>
    public DbSet<Project> Projects { get; set; } = null!;

    /// <summary>
    /// Tasks/etapas dos projetos
    /// </summary>
    public DbSet<ProjectTask> Tasks { get; set; } = null!;

    /// <summary>
    /// Histórico de avaliações de IA
    /// </summary>
    public DbSet<IaEvaluation> IaEvaluations { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ========== PROFILE ==========
        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("profiles");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedNever(); // ID vem do auth.users

            entity.Property(e => e.Username)
                .HasColumnName("username")
                .HasMaxLength(255);

            entity.Property(e => e.Email)
                .HasColumnName("email")
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("now()");

            // Indexes
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // ========== PROJECT ==========
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.OwnerId)
                .HasColumnName("owner_id")
                .IsRequired();

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Description)
                .HasColumnName("description")
                .HasMaxLength(400);

            entity.Property(e => e.Score)
                .HasColumnName("score")
                .HasPrecision(10, 2)
                .HasDefaultValue(0.0m);

            entity.Property(e => e.Valuation)
                .HasColumnName("valuation")
                .HasPrecision(15, 2)
                .HasDefaultValue(250.00m);

            entity.Property(e => e.ProgressBreakdown)
                .HasColumnName("progress_breakdown")
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb");

            entity.Property(e => e.CurrentPhase)
                .HasColumnName("current_phase")
                .HasMaxLength(50)
                .HasDefaultValue("fase1");

            entity.Property(e => e.Category)
                .HasColumnName("category")
                .HasMaxLength(100);

            entity.Property(e => e.GeneratedOptions)
                .HasColumnName("generated_options")
                .HasColumnType("text[]");

            entity.Property(e => e.ProductStructure)
                .HasColumnName("product_structure")
                .HasMaxLength(100);

            entity.Property(e => e.TargetAudience)
                .HasColumnName("target_audience")
                .HasMaxLength(100);

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("now()");

            // Relationships
            entity.HasOne(e => e.Owner)
                .WithMany(p => p.Projects)
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            entity.HasIndex(e => e.OwnerId);
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.CurrentPhase);
        });

        // ========== PROJECT TASK ==========
        modelBuilder.Entity<ProjectTask>(entity =>
        {
            entity.ToTable("tasks");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.ProjectId)
                .HasColumnName("project_id")
                .IsRequired();

            entity.Property(e => e.Title)
                .HasColumnName("title")
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Description)
                .HasColumnName("description");

            entity.Property(e => e.Phase)
                .HasColumnName("phase")
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Content)
                .HasColumnName("content");

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .HasDefaultValue("draft");

            entity.Property(e => e.EvaluationResult)
                .HasColumnName("evaluation_result")
                .HasColumnType("jsonb");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("now()");

            // Relationships
            entity.HasOne(e => e.Project)
                .WithMany(p => p.Tasks)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.Phase);
            entity.HasIndex(e => e.Status);
        });

        // ========== IA EVALUATION ==========
        modelBuilder.Entity<IaEvaluation>(entity =>
        {
            entity.ToTable("ia_evaluations");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.TaskId)
                .HasColumnName("task_id")
                .IsRequired();

            entity.Property(e => e.InputText)
                .HasColumnName("input_text");

            entity.Property(e => e.OutputJson)
                .HasColumnName("output_json")
                .HasColumnType("jsonb");

            entity.Property(e => e.ModelUsed)
                .HasColumnName("model_used")
                .HasMaxLength(100);

            entity.Property(e => e.TokensUsed)
                .HasColumnName("tokens_used");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            // Relationships
            entity.HasOne(e => e.Task)
                .WithMany(t => t.IaEvaluations)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.TaskId);
            entity.HasIndex(e => e.ModelUsed);
            entity.HasIndex(e => e.CreatedAt);
        });
    }

    /// <summary>
    /// Atualiza automaticamente UpdatedAt antes de salvar
    /// </summary>
    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// Atualiza automaticamente UpdatedAt antes de salvar (async)
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.Entity is Profile profile)
            {
                profile.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.Entity is Project project)
            {
                project.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.Entity is ProjectTask task)
            {
                task.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
