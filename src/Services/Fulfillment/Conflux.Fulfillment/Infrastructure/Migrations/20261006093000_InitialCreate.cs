using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conflux.Fulfillment.Infrastructure.Migrations;

/// <summary>Creates the Fulfillment service schema.</summary>
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "fulfillments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_fulfillments", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_fulfillments_OrderId", table: "fulfillments", column: "OrderId", unique: true);

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ConsumerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                EventType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Payload = table.Column<string>(type: "text", nullable: false),
                CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                CausationId = table.Column<Guid>(type: "uuid", nullable: true),
                ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_inbox_messages", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_inbox_messages_ConsumerName_Id", table: "inbox_messages", columns: new[] { "ConsumerName", "Id" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_inbox_messages_ConsumerName_ProcessedAt_ReceivedAt", table: "inbox_messages", columns: new[] { "ConsumerName", "ProcessedAt", "ReceivedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "inbox_messages");
        migrationBuilder.DropTable(name: "fulfillments");
    }
}
