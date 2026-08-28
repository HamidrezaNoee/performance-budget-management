using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pbm");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldValueJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValueJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssumptionDefinitions",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssumptionDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssumptionDefinitions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetModels",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetModels_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetScenarios",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetScenarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetScenarios_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Industry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Companies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsBaseCurrency = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Currencies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Dimensions",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsHierarchical = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dimensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dimensions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FxRateSources",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRateSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FxRateSources_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Kpis",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Minimum = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    Maximum = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    Frequency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FormulaExpression = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kpis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Kpis_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LicenseSubscriptions",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MaxCompanies = table.Column<int>(type: "int", nullable: false),
                    MaxUsers = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseSubscriptions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DeduplicationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboxMessage_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Roles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StrategicObjectives",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategicObjectives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategicObjectives_StrategicObjectives_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "pbm",
                        principalTable: "StrategicObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StrategicObjectives_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TokenVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Measures",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValueType = table.Column<int>(type: "int", nullable: false),
                    Aggregation = table.Column<int>(type: "int", nullable: false),
                    IsCalculated = table.Column<bool>(type: "bit", nullable: false),
                    FormulaExpression = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Measures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Measures_BudgetModels_BudgetModelId",
                        column: x => x.BudgetModelId,
                        principalSchema: "pbm",
                        principalTable: "BudgetModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FiscalYears",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JalaliYear = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalYears", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalYears_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationUnits",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UnitType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationUnits_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationUnits_OrganizationUnits_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "pbm",
                        principalTable: "OrganizationUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetModelDimensions",
                schema: "pbm",
                columns: table => new
                {
                    BudgetModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetModelDimensions", x => new { x.BudgetModelId, x.DimensionId });
                    table.ForeignKey(
                        name: "FK_BudgetModelDimensions_BudgetModels_BudgetModelId",
                        column: x => x.BudgetModelId,
                        principalSchema: "pbm",
                        principalTable: "BudgetModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetModelDimensions_Dimensions_DimensionId",
                        column: x => x.DimensionId,
                        principalSchema: "pbm",
                        principalTable: "Dimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DimensionMembers",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExternalKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DimensionMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DimensionMembers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DimensionMembers_DimensionMembers_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "pbm",
                        principalTable: "DimensionMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DimensionMembers_Dimensions_DimensionId",
                        column: x => x.DimensionId,
                        principalSchema: "pbm",
                        principalTable: "Dimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FxRates",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FxRates_Currencies_FromCurrencyId",
                        column: x => x.FromCurrencyId,
                        principalSchema: "pbm",
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FxRates_Currencies_ToCurrencyId",
                        column: x => x.ToCurrencyId,
                        principalSchema: "pbm",
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FxRates_FxRateSources_SourceId",
                        column: x => x.SourceId,
                        principalSchema: "pbm",
                        principalTable: "FxRateSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KpiObjectiveLinks",
                schema: "pbm",
                columns: table => new
                {
                    KpiId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiObjectiveLinks", x => new { x.KpiId, x.ObjectiveId });
                    table.ForeignKey(
                        name: "FK_KpiObjectiveLinks_Kpis_KpiId",
                        column: x => x.KpiId,
                        principalSchema: "pbm",
                        principalTable: "Kpis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KpiObjectiveLinks_StrategicObjectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalSchema: "pbm",
                        principalTable: "StrategicObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    RequestHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdempotencyRecords_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationCredentials",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SecretHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SecretSalt = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SecretIterations = table.Column<int>(type: "int", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ActionUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserCompanyAccess",
                schema: "pbm",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanRead = table.Column<bool>(type: "bit", nullable: false),
                    CanWrite = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCompanyAccess", x => new { x.UserId, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_UserCompanyAccess_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserCompanyAccess_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "pbm",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "pbm",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetPlans",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetPlans_BudgetModels_BudgetModelId",
                        column: x => x.BudgetModelId,
                        principalSchema: "pbm",
                        principalTable: "BudgetModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetPlans_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetPlans_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalSchema: "pbm",
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FiscalPeriods",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JalaliMonth = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalPeriods_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalSchema: "pbm",
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapexProjects",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectDimensionMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedBudget = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    ApprovedBudgetLimit = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    OwnerOrganizationUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletionPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    LastDecisionComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapexProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapexProjects_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapexProjects_DimensionMembers_ProjectDimensionMemberId",
                        column: x => x.ProjectDimensionMemberId,
                        principalSchema: "pbm",
                        principalTable: "DimensionMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapexProjects_OrganizationUnits_OwnerOrganizationUnitId",
                        column: x => x.OwnerOrganizationUnitId,
                        principalSchema: "pbm",
                        principalTable: "OrganizationUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapexProjects_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "pbm",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapexProjects_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapexProjects_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetVersions",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetVersions_BudgetPlans_BudgetPlanId",
                        column: x => x.BudgetPlanId,
                        principalSchema: "pbm",
                        principalTable: "BudgetPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetVersions_BudgetScenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalSchema: "pbm",
                        principalTable: "BudgetScenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssumptionValues",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScopeKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssumptionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssumptionValues_AssumptionDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "pbm",
                        principalTable: "AssumptionDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssumptionValues_BudgetScenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalSchema: "pbm",
                        principalTable: "BudgetScenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssumptionValues_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssumptionValues_FiscalPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalSchema: "pbm",
                        principalTable: "FiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssumptionValues_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalSchema: "pbm",
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KpiValues",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KpiId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Target = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    Actual = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KpiValues_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KpiValues_FiscalPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalSchema: "pbm",
                        principalTable: "FiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KpiValues_Kpis_KpiId",
                        column: x => x.KpiId,
                        principalSchema: "pbm",
                        principalTable: "Kpis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapexMilestones",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ProgressPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapexMilestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapexMilestones_CapexProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "pbm",
                        principalTable: "CapexProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActualLedgerEntries",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeasureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ExternalDocumentId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ExternalLineId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    PayloadHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    PostingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CoordinateHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CoordinatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActualLedgerEntries_ActualLedgerEntries_OriginalEntryId",
                        column: x => x.OriginalEntryId,
                        principalSchema: "pbm",
                        principalTable: "ActualLedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualLedgerEntries_BudgetVersions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "pbm",
                        principalTable: "BudgetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualLedgerEntries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualLedgerEntries_FiscalPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalSchema: "pbm",
                        principalTable: "FiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualLedgerEntries_Measures_MeasureId",
                        column: x => x.MeasureId,
                        principalSchema: "pbm",
                        principalTable: "Measures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualLedgerEntries_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetComments",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetComments_BudgetVersions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "pbm",
                        principalTable: "BudgetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetComments_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetFacts",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeasureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValueKind = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CoordinateHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CoordinatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetFacts_BudgetVersions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "pbm",
                        principalTable: "BudgetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetFacts_FiscalPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalSchema: "pbm",
                        principalTable: "FiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetFacts_Measures_MeasureId",
                        column: x => x.MeasureId,
                        principalSchema: "pbm",
                        principalTable: "Measures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetReservations",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeasureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReservationNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CoordinateHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CoordinatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DecisionComment = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetReservations_BudgetVersions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "pbm",
                        principalTable: "BudgetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetReservations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetReservations_FiscalPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalSchema: "pbm",
                        principalTable: "FiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetReservations_Measures_MeasureId",
                        column: x => x.MeasureId,
                        principalSchema: "pbm",
                        principalTable: "Measures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetReservations_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetReservations_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetTransfers",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeasureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourcePeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransferNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceCoordinateHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceCoordinatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DestinationCoordinateHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DestinationCoordinatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DecisionComment = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetTransfers_BudgetVersions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "pbm",
                        principalTable: "BudgetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransfers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "pbm",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransfers_FiscalPeriods_DestinationPeriodId",
                        column: x => x.DestinationPeriodId,
                        principalSchema: "pbm",
                        principalTable: "FiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransfers_FiscalPeriods_SourcePeriodId",
                        column: x => x.SourcePeriodId,
                        principalSchema: "pbm",
                        principalTable: "FiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransfers_Measures_MeasureId",
                        column: x => x.MeasureId,
                        principalSchema: "pbm",
                        principalTable: "Measures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransfers_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransfers_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActualLedgerDimensions",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualLedgerDimensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActualLedgerDimensions_ActualLedgerEntries_EntryId",
                        column: x => x.EntryId,
                        principalSchema: "pbm",
                        principalTable: "ActualLedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActualLedgerDimensions_DimensionMembers_MemberId",
                        column: x => x.MemberId,
                        principalSchema: "pbm",
                        principalTable: "DimensionMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualLedgerDimensions_Dimensions_DimensionId",
                        column: x => x.DimensionId,
                        principalSchema: "pbm",
                        principalTable: "Dimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetAttachments",
                schema: "pbm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetAttachments_BudgetComments_CommentId",
                        column: x => x.CommentId,
                        principalSchema: "pbm",
                        principalTable: "BudgetComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BudgetAttachments_BudgetVersions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "pbm",
                        principalTable: "BudgetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetAttachments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalSchema: "pbm",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetFactDimensions",
                schema: "pbm",
                columns: table => new
                {
                    BudgetFactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetFactDimensions", x => new { x.BudgetFactId, x.DimensionId });
                    table.ForeignKey(
                        name: "FK_BudgetFactDimensions_BudgetFacts_BudgetFactId",
                        column: x => x.BudgetFactId,
                        principalSchema: "pbm",
                        principalTable: "BudgetFacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetFactDimensions_DimensionMembers_MemberId",
                        column: x => x.MemberId,
                        principalSchema: "pbm",
                        principalTable: "DimensionMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetFactDimensions_Dimensions_DimensionId",
                        column: x => x.DimensionId,
                        principalSchema: "pbm",
                        principalTable: "Dimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetReservationDimensions",
                schema: "pbm",
                columns: table => new
                {
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetReservationDimensions", x => new { x.ReservationId, x.DimensionId });
                    table.ForeignKey(
                        name: "FK_BudgetReservationDimensions_BudgetReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalSchema: "pbm",
                        principalTable: "BudgetReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetReservationDimensions_DimensionMembers_MemberId",
                        column: x => x.MemberId,
                        principalSchema: "pbm",
                        principalTable: "DimensionMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetReservationDimensions_Dimensions_DimensionId",
                        column: x => x.DimensionId,
                        principalSchema: "pbm",
                        principalTable: "Dimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetTransferDimensions",
                schema: "pbm",
                columns: table => new
                {
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTransferDimensions", x => new { x.TransferId, x.DimensionId });
                    table.ForeignKey(
                        name: "FK_BudgetTransferDimensions_BudgetTransfers_TransferId",
                        column: x => x.TransferId,
                        principalSchema: "pbm",
                        principalTable: "BudgetTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetTransferDimensions_DimensionMembers_DestinationMemberId",
                        column: x => x.DestinationMemberId,
                        principalSchema: "pbm",
                        principalTable: "DimensionMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransferDimensions_DimensionMembers_SourceMemberId",
                        column: x => x.SourceMemberId,
                        principalSchema: "pbm",
                        principalTable: "DimensionMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransferDimensions_Dimensions_DimensionId",
                        column: x => x.DimensionId,
                        principalSchema: "pbm",
                        principalTable: "Dimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerDimensions_DimensionId",
                schema: "pbm",
                table: "ActualLedgerDimensions",
                column: "DimensionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerDimensions_EntryId_DimensionId",
                schema: "pbm",
                table: "ActualLedgerDimensions",
                columns: new[] { "EntryId", "DimensionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerDimensions_MemberId",
                schema: "pbm",
                table: "ActualLedgerDimensions",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerEntries_CompanyId",
                schema: "pbm",
                table: "ActualLedgerEntries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerEntries_CreatedByUserId",
                schema: "pbm",
                table: "ActualLedgerEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerEntries_MeasureId",
                schema: "pbm",
                table: "ActualLedgerEntries",
                column: "MeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerEntries_OriginalEntryId",
                schema: "pbm",
                table: "ActualLedgerEntries",
                column: "OriginalEntryId",
                unique: true,
                filter: "[OriginalEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerEntries_PeriodId",
                schema: "pbm",
                table: "ActualLedgerEntries",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerEntries_TenantId_CompanyId_CreatedAtUtc",
                schema: "pbm",
                table: "ActualLedgerEntries",
                columns: new[] { "TenantId", "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerEntries_TenantId_CompanyId_SourceSystem_ExternalDocumentId_ExternalLineId_EntryType",
                schema: "pbm",
                table: "ActualLedgerEntries",
                columns: new[] { "TenantId", "CompanyId", "SourceSystem", "ExternalDocumentId", "ExternalLineId", "EntryType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActualLedgerEntries_VersionId_PeriodId_MeasureId_CoordinateHash",
                schema: "pbm",
                table: "ActualLedgerEntries",
                columns: new[] { "VersionId", "PeriodId", "MeasureId", "CoordinateHash" });

            migrationBuilder.CreateIndex(
                name: "IX_AssumptionDefinitions_TenantId_Code",
                schema: "pbm",
                table: "AssumptionDefinitions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssumptionValues_CompanyId",
                schema: "pbm",
                table: "AssumptionValues",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_AssumptionValues_DefinitionId_CompanyId_FiscalYearId_ScopeKey",
                schema: "pbm",
                table: "AssumptionValues",
                columns: new[] { "DefinitionId", "CompanyId", "FiscalYearId", "ScopeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssumptionValues_FiscalYearId",
                schema: "pbm",
                table: "AssumptionValues",
                column: "FiscalYearId");

            migrationBuilder.CreateIndex(
                name: "IX_AssumptionValues_PeriodId",
                schema: "pbm",
                table: "AssumptionValues",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_AssumptionValues_ScenarioId",
                schema: "pbm",
                table: "AssumptionValues",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAttachments_CommentId",
                schema: "pbm",
                table: "BudgetAttachments",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAttachments_UploadedByUserId",
                schema: "pbm",
                table: "BudgetAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAttachments_VersionId_CreatedAtUtc",
                schema: "pbm",
                table: "BudgetAttachments",
                columns: new[] { "VersionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAttachments_VersionId_Sha256",
                schema: "pbm",
                table: "BudgetAttachments",
                columns: new[] { "VersionId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetComments_UserId",
                schema: "pbm",
                table: "BudgetComments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetComments_VersionId",
                schema: "pbm",
                table: "BudgetComments",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetFactDimensions_DimensionId",
                schema: "pbm",
                table: "BudgetFactDimensions",
                column: "DimensionId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetFactDimensions_MemberId",
                schema: "pbm",
                table: "BudgetFactDimensions",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetFacts_MeasureId",
                schema: "pbm",
                table: "BudgetFacts",
                column: "MeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetFacts_PeriodId",
                schema: "pbm",
                table: "BudgetFacts",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetFacts_VersionId_PeriodId_MeasureId_ValueKind_CoordinateHash",
                schema: "pbm",
                table: "BudgetFacts",
                columns: new[] { "VersionId", "PeriodId", "MeasureId", "ValueKind", "CoordinateHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetModelDimensions_DimensionId",
                schema: "pbm",
                table: "BudgetModelDimensions",
                column: "DimensionId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetModels_TenantId_Code",
                schema: "pbm",
                table: "BudgetModels",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetPlans_BudgetModelId",
                schema: "pbm",
                table: "BudgetPlans",
                column: "BudgetModelId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetPlans_CompanyId_FiscalYearId_BudgetModelId",
                schema: "pbm",
                table: "BudgetPlans",
                columns: new[] { "CompanyId", "FiscalYearId", "BudgetModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetPlans_FiscalYearId",
                schema: "pbm",
                table: "BudgetPlans",
                column: "FiscalYearId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservationDimensions_DimensionId",
                schema: "pbm",
                table: "BudgetReservationDimensions",
                column: "DimensionId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservationDimensions_MemberId",
                schema: "pbm",
                table: "BudgetReservationDimensions",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservations_CompanyId_ReservationNo",
                schema: "pbm",
                table: "BudgetReservations",
                columns: new[] { "CompanyId", "ReservationNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservations_DecidedByUserId",
                schema: "pbm",
                table: "BudgetReservations",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservations_MeasureId",
                schema: "pbm",
                table: "BudgetReservations",
                column: "MeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservations_PeriodId",
                schema: "pbm",
                table: "BudgetReservations",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservations_RequestedByUserId",
                schema: "pbm",
                table: "BudgetReservations",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservations_VersionId_PeriodId_MeasureId_CoordinateHash_Status",
                schema: "pbm",
                table: "BudgetReservations",
                columns: new[] { "VersionId", "PeriodId", "MeasureId", "CoordinateHash", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservations_VersionId_Status_CreatedAtUtc",
                schema: "pbm",
                table: "BudgetReservations",
                columns: new[] { "VersionId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetScenarios_TenantId_Code",
                schema: "pbm",
                table: "BudgetScenarios",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransferDimensions_DestinationMemberId",
                schema: "pbm",
                table: "BudgetTransferDimensions",
                column: "DestinationMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransferDimensions_DimensionId",
                schema: "pbm",
                table: "BudgetTransferDimensions",
                column: "DimensionId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransferDimensions_SourceMemberId",
                schema: "pbm",
                table: "BudgetTransferDimensions",
                column: "SourceMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransfers_CompanyId_TransferNo",
                schema: "pbm",
                table: "BudgetTransfers",
                columns: new[] { "CompanyId", "TransferNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransfers_DecidedByUserId",
                schema: "pbm",
                table: "BudgetTransfers",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransfers_DestinationPeriodId",
                schema: "pbm",
                table: "BudgetTransfers",
                column: "DestinationPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransfers_MeasureId",
                schema: "pbm",
                table: "BudgetTransfers",
                column: "MeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransfers_RequestedByUserId",
                schema: "pbm",
                table: "BudgetTransfers",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransfers_SourcePeriodId",
                schema: "pbm",
                table: "BudgetTransfers",
                column: "SourcePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransfers_VersionId_Status_CreatedAtUtc",
                schema: "pbm",
                table: "BudgetTransfers",
                columns: new[] { "VersionId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetVersions_BudgetPlanId_VersionNumber",
                schema: "pbm",
                table: "BudgetVersions",
                columns: new[] { "BudgetPlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetVersions_ScenarioId",
                schema: "pbm",
                table: "BudgetVersions",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_CapexMilestones_ProjectId_Code",
                schema: "pbm",
                table: "CapexMilestones",
                columns: new[] { "ProjectId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapexProjects_ApprovedByUserId",
                schema: "pbm",
                table: "CapexProjects",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CapexProjects_CompanyId_Code",
                schema: "pbm",
                table: "CapexProjects",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapexProjects_CompanyId_Status_IsActive",
                schema: "pbm",
                table: "CapexProjects",
                columns: new[] { "CompanyId", "Status", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CapexProjects_OwnerOrganizationUnitId",
                schema: "pbm",
                table: "CapexProjects",
                column: "OwnerOrganizationUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CapexProjects_ProjectDimensionMemberId",
                schema: "pbm",
                table: "CapexProjects",
                column: "ProjectDimensionMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_CapexProjects_RequestedByUserId",
                schema: "pbm",
                table: "CapexProjects",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CapexProjects_TenantId",
                schema: "pbm",
                table: "CapexProjects",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_TenantId_Code",
                schema: "pbm",
                table: "Companies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_TenantId_Code",
                schema: "pbm",
                table: "Currencies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DimensionMembers_CompanyId",
                schema: "pbm",
                table: "DimensionMembers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DimensionMembers_DimensionId_CompanyId_Code",
                schema: "pbm",
                table: "DimensionMembers",
                columns: new[] { "DimensionId", "CompanyId", "Code" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DimensionMembers_ParentId",
                schema: "pbm",
                table: "DimensionMembers",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Dimensions_TenantId_Code",
                schema: "pbm",
                table: "Dimensions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_FiscalYearId_Sequence",
                schema: "pbm",
                table: "FiscalPeriods",
                columns: new[] { "FiscalYearId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalYears_CompanyId_Code",
                schema: "pbm",
                table: "FiscalYears",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_FromCurrencyId",
                schema: "pbm",
                table: "FxRates",
                column: "FromCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_SourceId_FromCurrencyId_ToCurrencyId_RateDate",
                schema: "pbm",
                table: "FxRates",
                columns: new[] { "SourceId", "FromCurrencyId", "ToCurrencyId", "RateDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_ToCurrencyId",
                schema: "pbm",
                table: "FxRates",
                column: "ToCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_FxRateSources_TenantId_Code",
                schema: "pbm",
                table: "FxRateSources",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_TenantId_Status_ExpiresAtUtc",
                schema: "pbm",
                table: "IdempotencyRecords",
                columns: new[] { "TenantId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_TenantId_Status_UpdatedAtUtc",
                schema: "pbm",
                table: "IdempotencyRecords",
                columns: new[] { "TenantId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_TenantId_UserId_Scope_Key",
                schema: "pbm",
                table: "IdempotencyRecords",
                columns: new[] { "TenantId", "UserId", "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_UserId",
                schema: "pbm",
                table: "IdempotencyRecords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationCredentials_ClientId",
                schema: "pbm",
                table: "IntegrationCredentials",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationCredentials_TenantId_UserId_RevokedAtUtc_ExpiresAtUtc",
                schema: "pbm",
                table: "IntegrationCredentials",
                columns: new[] { "TenantId", "UserId", "RevokedAtUtc", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationCredentials_UserId",
                schema: "pbm",
                table: "IntegrationCredentials",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_KpiObjectiveLinks_ObjectiveId",
                schema: "pbm",
                table: "KpiObjectiveLinks",
                column: "ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_Kpis_TenantId_Code",
                schema: "pbm",
                table: "Kpis",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KpiValues_CompanyId",
                schema: "pbm",
                table: "KpiValues",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_KpiValues_KpiId_CompanyId_PeriodId",
                schema: "pbm",
                table: "KpiValues",
                columns: new[] { "KpiId", "CompanyId", "PeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KpiValues_PeriodId",
                schema: "pbm",
                table: "KpiValues",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseSubscriptions_TenantId",
                schema: "pbm",
                table: "LicenseSubscriptions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Measures_BudgetModelId_Code",
                schema: "pbm",
                table: "Measures",
                columns: new[] { "BudgetModelId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CompanyId",
                schema: "pbm",
                table: "Notifications",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_CompanyId_CreatedAtUtc",
                schema: "pbm",
                table: "Notifications",
                columns: new[] { "TenantId", "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead_CreatedAtUtc",
                schema: "pbm",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnits_CompanyId_Code",
                schema: "pbm",
                table: "OrganizationUnits",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnits_ParentId",
                schema: "pbm",
                table: "OrganizationUnits",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_TenantId",
                schema: "pbm",
                table: "OutboxMessage",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_Code",
                schema: "pbm",
                table: "Roles",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategicObjectives_ParentId",
                schema: "pbm",
                table: "StrategicObjectives",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategicObjectives_TenantId_Code",
                schema: "pbm",
                table: "StrategicObjectives",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Code",
                schema: "pbm",
                table: "Tenants",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCompanyAccess_CompanyId",
                schema: "pbm",
                table: "UserCompanyAccess",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                schema: "pbm",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_UserName",
                schema: "pbm",
                table: "Users",
                columns: new[] { "TenantId", "UserName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActualLedgerDimensions",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "AssumptionValues",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "AuditLogs",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetAttachments",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetFactDimensions",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetModelDimensions",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetReservationDimensions",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetTransferDimensions",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "CapexMilestones",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "FxRates",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "IntegrationCredentials",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "KpiObjectiveLinks",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "KpiValues",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "LicenseSubscriptions",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "OutboxMessage",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "UserCompanyAccess",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "ActualLedgerEntries",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "AssumptionDefinitions",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetComments",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetFacts",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetReservations",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetTransfers",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "CapexProjects",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "Currencies",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "FxRateSources",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "StrategicObjectives",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "Kpis",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetVersions",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "FiscalPeriods",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "Measures",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "DimensionMembers",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "OrganizationUnits",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetPlans",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetScenarios",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "Dimensions",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "BudgetModels",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "FiscalYears",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "Companies",
                schema: "pbm");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "pbm");
        }
    }
}
