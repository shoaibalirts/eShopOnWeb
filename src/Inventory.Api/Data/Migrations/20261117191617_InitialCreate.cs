using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Inventory.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockItems",
                columns: table => new
                {
                    CatalogItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItems", x => x.CatalogItemId);
                });

            migrationBuilder.InsertData(
                table: "StockItems",
                columns: new[] { "CatalogItemId", "Quantity" },
                values: new object[,]
                {
                    { 1, 25 },
                    { 2, 3 },
                    { 3, 25 },
                    { 4, 25 },
                    { 5, 25 },
                    { 6, 25 },
                    { 7, 25 },
                    { 8, 25 },
                    { 9, 25 },
                    { 10, 25 },
                    { 11, 25 },
                    { 12, 25 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockItems");
        }
    }
}
