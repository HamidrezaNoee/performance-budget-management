using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBM.Infrastructure.Migrations;

[DbContext(typeof(PbmDbContext))]
[Migration("20260830093000_OperationalMasterData")]
public partial class OperationalMasterData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "MetadataJson",
            schema: "pbm",
            table: "DimensionMembers",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "OrganizationUnitId",
            schema: "pbm",
            table: "UserCompanyAccess",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserCompanyAccess_OrganizationUnitId",
            schema: "pbm",
            table: "UserCompanyAccess",
            column: "OrganizationUnitId");

        migrationBuilder.AddForeignKey(
            name: "FK_UserCompanyAccess_OrganizationUnits_OrganizationUnitId",
            schema: "pbm",
            table: "UserCompanyAccess",
            column: "OrganizationUnitId",
            principalSchema: "pbm",
            principalTable: "OrganizationUnits",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_UserCompanyAccess_OrganizationUnits_OrganizationUnitId",
            schema: "pbm",
            table: "UserCompanyAccess");

        migrationBuilder.DropIndex(
            name: "IX_UserCompanyAccess_OrganizationUnitId",
            schema: "pbm",
            table: "UserCompanyAccess");

        migrationBuilder.DropColumn(
            name: "OrganizationUnitId",
            schema: "pbm",
            table: "UserCompanyAccess");

        migrationBuilder.DropColumn(
            name: "MetadataJson",
            schema: "pbm",
            table: "DimensionMembers");
    }
}
