# Waitlist Feature - Code Summary & Quick Reference

## Quick Start

### 1. Apply Database Migration
```bash
cd api-dotnet
dotnet ef database update
```

### 2. Run Tests
```bash
dotnet test
```

### 3. Start Application
```bash
dotnet run
```

### 4. Access API
- **Base URL**: http://localhost:5000
- **Swagger Docs**: http://localhost:5000/swagger
- **Health Check**: http://localhost:5000/health

---

## Files Created

### Domain Layer

#### 1. `src/Domain/Enums/WaitlistStatus.cs`
**Purpose**: Enumeration for waitlist entry lifecycle

**Enum Values**:
```csharp
public enum WaitlistStatus
{
    Pending = 0,           // Initial state
    EmailConfirmed = 1,    // Email verified
    Promoted = 2,          // Converted to user account
    Cancelled = 3          // Removed from waitlist
}
```

#### 2. `src/Domain/Entities/Waitlist.cs`
**Purpose**: Core domain entity with business logic

**Key Properties**:
- `Email`: Required, max 254 chars, unique, case-insensitive
- `CompanyName`: Optional, max 200 chars
- `Status`: WaitlistStatus enum, default Pending
- `Position`: Long, unique, sequential starting from 1
- `EmailConfirmed`: Boolean, default false
- `EmailConfirmationToken`: Secure token, max 500 chars
- `EmailConfirmedAt`: Nullable timestamp
- `InvitationToken`: Secure token, max 500 chars
- `Notes`: Optional text
- `PromotedUserId`: Nullable Guid reference to User
- `PromotedAt`: Nullable timestamp

**Key Methods**:
```csharp
// Factory method
public static Waitlist Create(string email, string? companyName, long position)

// Business logic
public void SetPosition(long position)
public void GenerateEmailConfirmationToken(string token)
public void ConfirmEmail()
public void ValidateEmailConfirmationToken(string token)
public void GenerateInvitationToken(string token)
public void MarkPromoted(Guid userId)
public void MarkCancelled()
public void UpdateNotes(string notes)
public void UpdateCompanyName(string companyName)
```

---

### Application Layer

#### 1. `src/Application/Features/Waitlist/DTOs/WaitlistDtos.cs`
**Purpose**: Request/response contracts

**DTOs**:
```csharp
public record JoinWaitlistRequest(string Email, string? CompanyName = null);
public record WaitlistResponse(string Email, string? CompanyName, long Position, WaitlistStatus Status, bool EmailConfirmed);
public record WaitlistStatusResponse(WaitlistStatus Status, long Position, long TotalCount);
public record WaitlistListItemDto(Guid Id, string Email, long Position, WaitlistStatus Status, string? CompanyName, DateTime? EmailConfirmedAt, DateTime CreatedAt);
public record WaitlistAnalyticsDto(long TotalCount, long PendingCount, long EmailConfirmedCount, long PromotedCount, long CancelledCount, double PromotionRate, double CancellationRate, double AverageDaysToPromotion, List<(string CompanyName, int Count)> TopCompanies);
public record PromoteWaitlistResponse(Guid NewUserId, string Email, string Message);
public record CancelWaitlistRequest(string Email);
public record VerifyWaitlistEmailRequest(string Email, string Token);
```

#### 2. `src/Application/Features/Waitlist/Validators/WaitlistValidators.cs`
**Purpose**: Input validation using FluentValidation

**Validators**:
- `JoinWaitlistValidator`: Email required, valid format, max 254 chars; CompanyName max 200
- `VerifyWaitlistEmailValidator`: Email and token required
- `CancelWaitlistValidator`: Email required

#### 3. `src/Application/Features/Waitlist/Commands/`

**JoinWaitlist.cs**:
- Validates duplicate email and registered user
- Gets next position
- Creates entry with token
- Sends confirmation email
- Returns WaitlistResponse

**VerifyWaitlistEmail.cs**:
- Validates token
- Confirms email
- Clears token
- Returns success message

**CancelWaitlist.cs**:
- Prevents cancellation if Promoted or already Cancelled
- Marks as Cancelled
- Returns success message

**PromoteWaitlist.cs**:
- Only works for EmailConfirmed entries
- Creates User account
- Sends invitation email with password reset
- Updates status and PromotedUserId
- Returns PromoteWaitlistResponse

**UpdateWaitlist.cs**:
- Updates CompanyName, Notes, or Status
- Prevents status changes for Promoted/Cancelled
- Returns updated WaitlistListItemDto

**DeleteWaitlist.cs**:
- Hard delete from database
- Admin only
- Returns success message

#### 4. `src/Application/Features/Waitlist/Queries/`

**GetWaitlistStatus.cs**:
- Public endpoint
- Case-insensitive email lookup
- Returns position and total count

**GetWaitlistList.cs**:
- Admin endpoint
- Pagination: page, pageSize (max 500)
- Filtering: status, email (contains)
- Sorting: position, email, status, createdAt
- Returns PagedResult<WaitlistListItemDto>

**GetWaitlistAnalytics.cs**:
- Admin endpoint
- Calculates counts by status
- Calculates rates (promotion, cancellation)
- Calculates average days to promotion
- Returns top 10 companies

---

### Infrastructure Layer

#### 1. `src/Infrastructure/Persistence/Repositories/WaitlistRepository.cs`
**Purpose**: EF Core implementation of IWaitlistRepository

**Methods**:
```csharp
public async Task<Waitlist?> FindByEmail(string email, CancellationToken ct)
public async Task<long> GetNextPosition(CancellationToken ct)
public async Task<long> GetTotalCount(CancellationToken ct)
public async Task<long> GetPositionByEmail(string email, CancellationToken ct)
public async Task<(List<Waitlist> items, long totalCount)> GetPaged(
    int page, int pageSize, 
    WaitlistStatus? statusFilter = null, 
    string? emailFilter = null, 
    string sortBy = "position", 
    string sortOrder = "asc", 
    CancellationToken ct = default)
public async Task<long> CountByStatus(WaitlistStatus status, CancellationToken ct)
public async Task<long> CountCreatedSince(DateTime date, CancellationToken ct)
public async Task<long> CountPromotedSince(DateTime date, CancellationToken ct)
public async Task<double> GetAverageDaysToPromotion(CancellationToken ct)
public async Task<List<(string CompanyName, int Count)>> GetTopCompanies(CancellationToken ct, int limit = 10)
public async Task<bool> ExistsByEmail(string email, CancellationToken ct)
public async Task<bool> ExistsByPromotedUserId(Guid userId, CancellationToken ct)
```

#### 2. `src/Infrastructure/Migrations/20260618_AddWaitlistFeatures.cs`
**Purpose**: EF Core migration

**Changes**:
- Adds Waitlists table with 9 new columns
- Creates 5 indexes
- Complete rollback capability in Down method

---

### Web Layer

#### 1. `src/Web/Controllers/WaitlistController.cs`
**Purpose**: HTTP API endpoints

**Endpoints** (9 total):

| Method | Endpoint | Auth | Rate Limit | Description |
|--------|----------|------|-----------|-------------|
| POST | /api/waitlist/join | Public | Yes | Join waitlist |
| GET | /api/waitlist/status | Public | Yes | Check position |
| GET | /api/waitlist/verify | Public | Yes | Verify email |
| POST | /api/waitlist/cancel | Public | Yes | Cancel waitlist |
| GET | /api/admin/waitlist/list | Admin | Yes | List entries |
| POST | /api/admin/waitlist/{id}/promote | Admin | Yes | Promote to user |
| PUT | /api/admin/waitlist/{id} | Admin | Yes | Update entry |
| DELETE | /api/admin/waitlist/{id} | Admin | Yes | Delete entry |
| GET | /api/admin/waitlist/analytics | Admin | Yes | Get metrics |

---

### Test Layer

#### 1. `src/Tests/Application/Waitlist/Commands/`

**JoinWaitlistHandlerTests.cs**:
- Valid email creation
- Duplicate email rejection
- Registered user rejection
- Email service failure handling

**VerifyWaitlistEmailHandlerTests.cs**:
- Valid token confirmation
- Invalid token rejection
- Non-existent email handling
- Already confirmed rejection

**CancelWaitlistHandlerTests.cs**:
- Valid cancellation
- Non-existent entry handling
- Already cancelled rejection
- Promoted entry rejection
- Case-insensitive email handling

#### 2. `src/Tests/Application/Waitlist/Queries/`

**GetWaitlistStatusHandlerTests.cs**:
- Valid email lookup
- Non-existent email handling
- Case-insensitive search
- All status types

---

## Files Modified

### `src/Web/Program.cs`
**Change**: Added service registration
```csharp
builder.Services.AddScoped<IWaitlistRepository, WaitlistRepository>();
```

### `src/Application/Interfaces/IRepositories.cs`
**Change**: Added IWaitlistRepository interface
```csharp
public interface IWaitlistRepository : IRepository<Waitlist>
{
    // 15 methods for queries
}
```

### `src/Infrastructure/Persistence/VulnWatchDbContext.cs`
**Change**: Added Waitlist entity configuration
```csharp
builder.Entity<Waitlist>(e => {
    e.HasKey(w => w.Id);
    e.Property(w => w.Email).IsRequired().HasMaxLength(254);
    e.Property(w => w.Status).HasMaxLength(50);
    e.HasIndex(w => w.Email).IsUnique();
    e.HasIndex(w => w.Status);
    e.HasIndex(w => w.Position).IsUnique();
    e.HasIndex(w => w.CreatedAt);
    e.HasIndex(w => w.PromotedUserId);
});
```

---

## Database Schema

### Waitlists Table
```sql
CREATE TABLE "Waitlists" (
  "Id" uuid PRIMARY KEY,
  "Email" varchar(254) UNIQUE NOT NULL,
  "CompanyName" varchar(200),
  "Status" varchar(50) DEFAULT 'Pending',
  "Position" bigint UNIQUE NOT NULL,
  "EmailConfirmed" boolean DEFAULT false,
  "EmailConfirmationToken" varchar(500),
  "EmailConfirmedAt" timestamp,
  "InvitationToken" varchar(500),
  "Notes" text,
  "PromotedUserId" uuid,
  "PromotedAt" timestamp,
  "CreatedAt" timestamp NOT NULL,
  "UpdatedAt" timestamp NOT NULL
);

CREATE UNIQUE INDEX IX_Waitlists_Email ON "Waitlists"("Email");
CREATE INDEX IX_Waitlists_Status ON "Waitlists"("Status");
CREATE UNIQUE INDEX IX_Waitlists_Position ON "Waitlists"("Position");
CREATE INDEX IX_Waitlists_CreatedAt ON "Waitlists"("CreatedAt");
CREATE INDEX IX_Waitlists_PromotedUserId ON "Waitlists"("PromotedUserId");
```

---

## Configuration Required

### appsettings.json

```json
{
  "FrontendUrl": {
    "Base": "http://localhost:3000",
    "WaitlistVerify": "http://localhost:3000/verify",
    "PasswordReset": "http://localhost:3000/reset-password"
  },
  "EmailService": {
    "ApiKey": "your_api_key",
    "FromEmail": "noreply@vulnwatch.com"
  }
}
```

---

## Key Design Patterns Used

### 1. **CQRS Pattern**
- Commands for write operations (Join, Cancel, Promote, etc.)
- Queries for read operations (GetStatus, GetList, GetAnalytics)
- Clear separation of concerns

### 2. **Repository Pattern**
- `IWaitlistRepository` interface abstracts data access
- `WaitlistRepository` implements EF Core access
- Enables testing with mocks

### 3. **Result Monad Pattern**
- All operations return `Result<T>` or `MessageResult`
- Consistent error handling across application
- Eliminates exceptions for expected error cases

### 4. **Factory Pattern**
- `Waitlist.Create()` factory method
- Encapsulates complex object construction
- Ensures valid initial state

### 5. **MediatR Command/Query Pattern**
- All handlers implement `IRequestHandler<T>`
- Dependency injection through constructor
- Automatic validation pipeline via behaviors

### 6. **Entity-Level Business Logic**
- Core rules embedded in domain entity methods
- Cannot create invalid states
- Business logic not scattered across application

---

## Error Handling

### HTTP Status Codes
- **200 OK**: Success
- **400 Bad Request**: Validation failed
- **401 Unauthorized**: Authentication required
- **403 Forbidden**: Authorization failed
- **404 Not Found**: Resource not found
- **409 Conflict**: Invalid state/duplicate
- **500 Internal Server Error**: Unexpected error

### Error Response Format
```json
{
  "success": false,
  "error": {
    "code": "ErrorCode",
    "message": "Human-readable message",
    "metadata": {
      "field": "fieldName",
      "value": "providedValue"
    }
  }
}
```

---

## Testing Strategy

### Unit Tests
- Test individual handlers in isolation
- Mock repositories and external services
- Verify business logic
- Run: `dotnet test`

### Integration Tests
- Test full workflows
- Use real database (test database)
- Verify end-to-end behavior
- Run: `dotnet test --filter "Integration"`

### API Tests
- Use Postman or curl
- Verify HTTP contracts
- Test authorization
- Verify error responses

---

## Performance Notes

### Database Queries
- **FindByEmail**: Uses indexed Email column
- **GetPaged**: Uses Status/Email filters with indexes
- **GetNextPosition**: MAX aggregation (O(1) on indexed column)
- **GetAverageDaysToPromotion**: Only queries Promoted entries

### Pagination
- Max pageSize: 500
- Skip/take LINQ operations
- Database-level pagination (not in-memory)

### Concurrency
- All operations async/await
- Proper CancellationToken handling
- No blocking calls

---

## Security Checklist

- [x] Email tokens: Cryptographically random (32 bytes, Base64URL)
- [x] Admin endpoints: Role-based authorization
- [x] Rate limiting: All endpoints
- [x] Input validation: FluentValidation
- [x] SQL injection prevention: LINQ parameterization
- [x] Case-insensitive email: Prevents duplicate emails
- [x] Password reset: Token-based, secure

---

## Migration Rollback

If needed to rollback the migration:

```bash
# List all migrations
dotnet ef migrations list

# Revert to previous migration
dotnet ef database update <previous_migration_name>

# Delete migration (if not applied)
dotnet ef migrations remove
```

---

## Next Steps

1. **Apply migration**: `dotnet ef database update`
2. **Run tests**: `dotnet test`
3. **Configure email service**: Update appsettings.json
4. **Manual testing**: Use verification checklist
5. **Load testing**: Performance baseline
6. **Security audit**: Penetration testing
7. **Documentation**: Update solution README

---

## Support References

- **EF Core Docs**: https://learn.microsoft.com/en-us/ef/core/
- **MediatR**: https://github.com/jbogard/MediatR
- **FluentValidation**: https://docs.fluentvalidation.net/
- **ASP.NET Core**: https://learn.microsoft.com/en-us/aspnet/core/

---

## Version Information

- **.NET**: 8.0
- **C#**: 12
- **EF Core**: 8.0.11
- **PostgreSQL**: 13+
- **MediatR**: Latest
- **FluentValidation**: Latest

---

**Last Updated**: June 18, 2026
**Status**: Implementation Complete, Ready for Testing
