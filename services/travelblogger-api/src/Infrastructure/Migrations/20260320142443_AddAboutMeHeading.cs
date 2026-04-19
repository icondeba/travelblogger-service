using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelBlogger.src.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAboutMeHeading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Heading",
                table: "AboutMe",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Heading",
                table: "AboutMe");
        }
    }
}
