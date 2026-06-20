# Waitlist Feature Specification

**Document Status:** Analysis Complete - Ready for Implementation  
**Project:** Vulnwatch (Domain Security Scanning Platform)  
**Last Updated:** 2026-06-18  

---

## 1. Executive Summary

The Waitlist feature enables early-stage users to express interest in Vulnwatch services before full access is available. Users can submit their email and company information to join a waitlist, receive position confirmation, and eventually be invited to use the platform. This feature supports business objectives of:

- Building an engaged user base pre-launch
- Capturing lead information (email, company)
- Enabling user segmentation and communication
- Managing onboarding of new users in phases

**Current State:** Waitlist entity exists in the domain (`Domain.Entities.Waitlist`) with basic structure:
- `Id` (Guid, primary key)
- `Email` (string, required)
- `CompanyName` (string, optional)
- `CreatedAt` (DateTime, timestamps)
- `UpdatedAt` (DateTime, optional)

**Scope:** This specification defines all changes required to make the waitlist feature fully functional with proper API endpoints, validations, services, and business logic.

---

## 2. Feature Overview

### 2.1 Core Capabilities

1. **Join Waitlist** - Unauthenticated users can submit email + optional company name
2. **Waitlist Position** - Users receive a unique position number on the waitlist
3. **Waitlist Status** - Users can check their position/status without authentication
4. **Duplicate Prevention** - Same email cannot join twice (prevent duplicates)
5. **Admin Promotion** - Admins can promote waitlist entries to full user accounts
6. **Analytics** - Track waitlist metrics (total entries, conversion rate, etc.)

### 2.2 User Workflows

**Workflow 1: Prospective User Joins Waitlist**
```
Prospective User
    ↓ (Submit email + company via public endpoint)
    ↓ Validation (email format, not already on waitlist)
    ↓ Save to Waitlist table
    ↓ Return position number + confirmation
    ↓ Send confirmation email
```

**Workflow 2: User Checks Waitlist Position**
```
Prospective User
    ↓ (Query with email, no auth required)
    ↓ Retrieve position from waitlist
    ↓ Return position + metadata
```

**Workflow 3: Admin Promotes User to Full Account**
```
Admin
    ↓ (Initiate promotion via protected endpoint)
    ↓ Retrieve waitlist entry by ID
    ↓ Create User account from waitlist data
    ↓ Send invitation email to new user
    ↓ Mark waitlist entry as promoted
```

---

## 3. Data Model

### 3.1 Updated Waitlist Entity

**File:** `api-dotnet/src/Domain/Entities/Waitlist.cs`

**Current Properties:**
```
- Id: Guid (Primary Key, auto-generated)
- Email: string (Required, max 254 chars)
- CompanyName: string (Optional, nullable)
- CreatedAt: DateTime (Timestamp, UTC)
- UpdatedAt: DateTime (Optional, timestamp)
```

**Required Additions:**

| Property | Type | Nullable | Purpose | Default |
|----------|------|----------|---------|---------|
| `Status` | enum `WaitlistStatus` | No | Current state of entry | `Pending` |
| `Position` | long | No | Sequential position in queue (for ordering) | Auto-assigned |
| `EmailConfirmed` | bool | No | Whether user confirmed email | `false` |
| `EmailConfirmationToken` | string | Yes | Token for email verification | null |
| `EmailConfirmedAt` | DateTime | Yes | When email was confirmed | null |
| `InvitationToken` | string | Yes | Token for converting to user account | null |
| `PromotedUserId` | Guid | Yes | FK to User if promoted | null |
| `PromotedAt` | DateTime | Yes | When promoted to full account | null |
| `Notes` | string | Yes | Internal admin notes | null |

**New Enum: `WaitlistStatus`**

**File:** `api-dotnet/src/Domain/Enums/WaitlistStatus.cs`

```
Pending          - Awaiting email confirmation
EmailConfirmed   - Email verified, waiting for promotion
Promoted         - User account created, moved off waitlist
Cancelled        - User requested removal
```

### 3.2 Entity Behavior

**Waitlist.cs Factory Methods:**
```csharp
// Create new waitlist entry
static Waitlist Create(string email, string? companyName = null)

// Generate email confirmation token
void GenerateEmailConfirmationToken()

// Confirm email
void ConfirmEmail()

// Generate invitation token (before promotion)
void GenerateInvitationToken()

// Mark as promoted with user reference
void MarkPromoted(Guid userId)

// Mark as cancelled
void MarkCancelled()

// Update position (batch operation by worker)
void UpdatePosition(long newPosition)
```

**Validation Rules (Domain Layer):**
- Email must be valid format (RFC 5322 simplified)
- Email must not be longer than 254 characters
- CompanyName if provided must be ≤200 characters
- Status transitions must follow rules:
  - `Pending` → `EmailConfirmed` (after email confirmation)
  - `Pending` → `Cancelled` (user request)
  - `EmailConfirmed` → `Promoted` (admin action)
  - `EmailConfirmed` → `Cancelled` (user request)
  - No transitions from `Promoted` or `Cancelled`

---

## 4. API Endpoints

### 4.1 Public Endpoints (No Authentication Required)

#### POST /api/waitlist/join
**Purpose:** Submit email + company to join waitlist

**Request:**
```json
{
  "email": "user@company.com",
  "companyName": "Acme Corp"
}
```

**Response (200 Created):**
```json
{
  "success": true,
  "message": "Successfully added to waitlist",
  "data": {
    "email": "user@company.com",
    "position": 1523,
    "status": "Pending",
    "createdAt": "2026-06-18T10:30:00Z"
  }
}
```

**Response (400 Bad Request - Invalid Email):**
```json
{
  "success": false,
  "error": {
    "code": "ValidationError",
    "message": "Invalid email format"
  }
}
```

**Response (409 Conflict - Already on Waitlist):**
```json
{
  "success": false,
  "error": {
    "code": "Conflict",
    "message": "Email already on waitlist",
    "data": {
      "position": 523,
      "status": "EmailConfirmed"
    }
  }
}
```

**Validation:**
- Email format validation (FluentValidation)
- Email not already on waitlist (case-insensitive check)
- Email not already a registered user
- CompanyName max length 200 chars

**Rate Limiting:** Standard endpoint rate limit (configured in Program.cs)

---

#### GET /api/waitlist/status
**Purpose:** Check waitlist status without authentication

**Request Parameters:**
- `email` (query param, format: `?email=value`) - Email address to check

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "email": "user@company.com",
    "position": 1523,
    "totalOnWaitlist": 2841,
    "status": "EmailConfirmed",
    "emailConfirmed": true,
    "joinedAt": "2026-06-15T14:22:00Z"
  }
}
```

**Response (404 Not Found):**
```json
{
  "success": false,
  "error": {
    "code": "NotFound",
    "message": "Email not found on waitlist"
  }
}
```

**Validation:**
- Email format validation

---

#### GET /api/waitlist/verify
**Purpose:** Verify email confirmation token and mark email as confirmed

**Request Parameters:**
- `email` (query) - User email
- `token` (query) - Confirmation token

**Response (200 OK):**
```json
{
  "success": true,
  "message": "Email successfully verified",
  "data": {
    "email": "user@company.com",
    "status": "EmailConfirmed",
    "position": 1523
  }
}
```

**Response (400 Bad Request - Invalid/Expired Token):**
```json
{
  "success": false,
  "error": {
    "code": "ValidationError",
    "message": "Invalid or expired verification token"
  }
}
```

---

#### POST /api/waitlist/cancel
**Purpose:** Remove email from waitlist (user-initiated)

**Request:**
```json
{
  "email": "user@company.com",
  "token": "cancellation-token-if-required"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "message": "Successfully removed from waitlist"
}
```

**Response (404 Not Found):**
```json
{
  "success": false,
  "error": {
    "code": "NotFound",
    "message": "Email not found on waitlist"
  }
}
```

---

### 4.2 Protected Endpoints (Admin/Authenticated Users)

#### GET /api/waitlist/admin/list
**Purpose:** List all waitlist entries with filtering/pagination

**Authentication Required:** Yes (Admin role or elevated permission)

**Query Parameters:**
- `status` (optional) - Filter by WaitlistStatus
- `page` (optional, default=1) - Page number
- `pageSize` (optional, default=50) - Items per page
- `searchEmail` (optional) - Search by partial email
- `sortBy` (optional) - Sort field: `position`, `createdAt`, `status`
- `sortOrder` (optional) - `asc` or `desc`

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "550e8400-e29b-41d4-a716-446655440000",
        "email": "user@company.com",
        "companyName": "Acme Corp",
        "position": 1,
        "status": "EmailConfirmed",
        "emailConfirmed": true,
        "createdAt": "2026-06-15T14:22:00Z",
        "emailConfirmedAt": "2026-06-16T10:00:00Z"
      },
      {
        "id": "660f9511-f3ac-52e5-b827-557766551111",
        "email": "another@business.com",
        "companyName": null,
        "position": 2,
        "status": "Pending",
        "emailConfirmed": false,
        "createdAt": "2026-06-17T08:30:00Z",
        "emailConfirmedAt": null
      }
    ],
    "pagination": {
      "page": 1,
      "pageSize": 50,
      "totalCount": 2841,
      "totalPages": 57
    }
  }
}
```

---

#### POST /api/waitlist/admin/{waitlistId}/promote
**Purpose:** Promote waitlist entry to full user account

**Authentication Required:** Yes (Admin role)

**Query Parameters:**
- `firstName` (optional) - First name for the new user
- `lastName` (optional) - Last name for the new user
- `sendInvitationEmail` (optional, default: `true`) - Whether to send the password setup invitation email

**Example:**
```http
POST /api/waitlist/admin/{waitlistId}/promote?firstName=John&lastName=Doe&sendInvitationEmail=true
```

**Response (200 OK):**
```json
{
  "success": true,
  "message": "User successfully promoted from waitlist",
  "data": {
    "waitlistId": "550e8400-e29b-41d4-a716-446655440000",
    "newUserId": "770g0622-g4bd-63f6-c939-778877662222",
    "email": "user@company.com",
    "status": "Promoted",
    "promotedAt": "2026-06-18T10:30:00Z"
  }
}
```

**Response (400 Bad Request - Not Confirmed):**
```json
{
  "success": false,
  "error": {
    "code": "ValidationError",
    "message": "Cannot promote: email not confirmed"
  }
}
```

**Response (409 Conflict - Already User):**
```json
{
  "success": false,
  "error": {
    "code": "Conflict",
    "message": "Email is already a registered user"
  }
}
```

**Business Logic:**
1. Validate waitlist entry exists and status is `EmailConfirmed`
2. Check email not already registered as User
3. Create User account using email from waitlist
4. Set User.FirstName, User.LastName from query parameters (or null)
5. Mark email as confirmed on User
6. Update Waitlist entry: status = `Promoted`, PromotedUserId = new User.Id
7. Send invitation email (if flag set) with temporary login link/password reset token
8. Return success with new User ID

---

#### PUT /api/waitlist/admin/{waitlistId}
**Purpose:** Update waitlist entry metadata

**Authentication Required:** Yes (Admin role)

**Request:**
```json
{
  "companyName": "Updated Company Name",
  "notes": "Admin notes about this user",
  "status": "Cancelled"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "message": "Waitlist entry updated",
  "data": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "email": "user@company.com",
    "companyName": "Updated Company Name",
    "notes": "Admin notes about this user",
    "status": "Cancelled",
    "updatedAt": "2026-06-18T11:00:00Z"
  }
}
```

---

#### DELETE /api/waitlist/admin/{waitlistId}
**Purpose:** Permanently delete waitlist entry (hard delete)

**Authentication Required:** Yes (Admin role)

**Response (204 No Content)**

---

#### GET /api/waitlist/admin/analytics
**Purpose:** Get waitlist analytics/metrics

**Authentication Required:** Yes (Admin role)

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "totalOnWaitlist": 2841,
    "statusBreakdown": {
      "pending": 1200,
      "emailConfirmed": 1400,
      "promoted": 200,
      "cancelled": 41
    },
    "joinedLast7Days": 350,
    "joinedLast30Days": 950,
    "promotionRate": "7.05%",
    "cancellationRate": "1.44%",
    "averageDaysToPromotion": 12.5,
    "topCompanies": [
      { "company": "Tech Corp", "count": 45 },
      { "company": "Secure Inc", "count": 38 }
    ]
  }
}
```

---

## 5. Application Layer (MediatR Handlers)

### 5.1 Handler Organization

**Directory:** `api-dotnet/src/Application/Features/Waitlist/`

**Structure:**
```
Waitlist/
  ├── DTOs/
  │   ├── JoinWaitlistRequest.cs
  │   ├── WaitlistResponse.cs
  │   ├── WaitlistListItemDto.cs
  │   └── WaitlistAnalyticsDto.cs
  ├── Queries/
  │   ├── GetWaitlistStatusQuery.cs
  │   ├── GetWaitlistStatusHandler.cs
  │   ├── GetWaitlistListQuery.cs (admin)
  │   ├── GetWaitlistListHandler.cs (admin)
  │   ├── GetWaitlistAnalyticsQuery.cs (admin)
  │   └── GetWaitlistAnalyticsHandler.cs (admin)
  ├── Commands/
  │   ├── JoinWaitlistCommand.cs
  │   ├── JoinWaitlistHandler.cs
  │   ├── VerifyWaitlistEmailCommand.cs
  │   ├── VerifyWaitlistEmailHandler.cs
  │   ├── CancelWaitlistCommand.cs
  │   ├── CancelWaitlistHandler.cs
  │   ├── PromoteWaitlistCommand.cs (admin)
  │   ├── PromoteWaitlistHandler.cs (admin)
  │   ├── UpdateWaitlistCommand.cs (admin)
  │   └── UpdateWaitlistHandler.cs (admin)
  └── Validators/
      ├── JoinWaitlistValidator.cs
      ├── VerifyWaitlistEmailValidator.cs
      └── CancelWaitlistValidator.cs
```

### 5.2 Command/Query Contracts

**JoinWaitlistCommand**
```csharp
public record JoinWaitlistCommand(string Email, string? CompanyName = null) 
    : IRequest<Result<WaitlistResponse>>;
```

**GetWaitlistStatusQuery**
```csharp
public record GetWaitlistStatusQuery(string Email) 
    : IRequest<Result<WaitlistResponse>>;
```

**VerifyWaitlistEmailCommand**
```csharp
public record VerifyWaitlistEmailCommand(string Email, string Token) 
    : IRequest<Result<MessageResponse>>;
```

**CancelWaitlistCommand**
```csharp
public record CancelWaitlistCommand(string Email) 
    : IRequest<Result<MessageResponse>>;
```

**PromoteWaitlistCommand (Admin)**
```csharp
public record PromoteWaitlistCommand(Guid WaitlistId, string? FirstName = null, string? LastName = null, bool SendInvitationEmail = true) 
    : IRequest<Result<PromoteWaitlistResponse>>;
```

**GetWaitlistListQuery (Admin)**
```csharp
public record GetWaitlistListQuery(
    WaitlistStatus? Status = null,
    int Page = 1,
    int PageSize = 50,
    string? SearchEmail = null,
    string SortBy = "position",
    string SortOrder = "asc"
) : IRequest<Result<PagedResult<WaitlistListItemDto>>>;
```

### 5.3 Handler Implementation Details

**JoinWaitlistHandler Must:**
1. Validate email format
2. Check if email already on waitlist (case-insensitive)
3. Check if email already a registered User
4. Create new Waitlist entry with Status = `Pending`
5. Generate email confirmation token
6. Calculate position (count + 1 from existing entries)
7. Send confirmation email
8. Return WaitlistResponse with position

**VerifyWaitlistEmailHandler Must:**
1. Find Waitlist by email
2. Validate token (compare with stored hash)
3. Check token not expired (valid for 7 days)
4. Update status to `EmailConfirmed`
5. Clear confirmation token
6. Return success

**PromoteWaitlistHandler Must:**
1. Validate user has Admin role
2. Fetch Waitlist entry
3. Check status is `EmailConfirmed`
4. Check email not already registered as User
5. Create User account from Waitlist data
6. Set User.FirstName/LastName (if provided)
7. Generate and send invitation email with password reset/temp token
8. Update Waitlist: status = `Promoted`, PromotedUserId = User.Id
9. Commit transaction (all-or-nothing)
10. Return new User ID

---

## 6. Repository Interface

### 6.1 IWaitlistRepository

**File:** `api-dotnet/src/Application/Interfaces/IRepositories.cs` (add new interface)

```csharp
public interface IWaitlistRepository : IRepository<Waitlist>
{
    // Queries
    Task<Waitlist?> FindByEmail(string email, CancellationToken ct);
    Task<Waitlist?> GetById(Guid id, CancellationToken ct);
    Task<long> GetNextPosition(CancellationToken ct);
    Task<long> GetPositionByEmail(string email, CancellationToken ct);
    Task<int> GetTotalCount(CancellationToken ct);
    Task<(List<Waitlist> Items, int TotalCount)> GetPaged(
        WaitlistStatus? status,
        int page,
        int pageSize,
        string? searchEmail,
        string sortBy,
        string sortOrder,
        CancellationToken ct);
    
    // Analytics
    Task<WaitlistAnalytics> GetAnalytics(CancellationToken ct);
    Task<int> CountByStatus(WaitlistStatus status, CancellationToken ct);
    Task<int> CountCreatedSince(DateTime since, CancellationToken ct);
    Task<int> CountPromotedSince(DateTime since, CancellationToken ct);
    
    // Utility
    Task<bool> ExistsByEmail(string email, CancellationToken ct);
    Task<bool> ExistsByPromotedUserId(Guid userId, CancellationToken ct);
}

public class WaitlistAnalytics
{
    public int TotalOnWaitlist { get; set; }
    public int PendingCount { get; set; }
    public int EmailConfirmedCount { get; set; }
    public int PromotedCount { get; set; }
    public int CancelledCount { get; set; }
    public decimal PromotionRate { get; set; }
    public decimal CancellationRate { get; set; }
    public double AverageDaysToPromotion { get; set; }
}
```

### 6.2 Implementation (Infrastructure Layer)

**File:** `api-dotnet/src/Infrastructure/Persistence/Repositories/WaitlistRepository.cs` (NEW)

**Key Implementation Points:**
- Use EF Core with proper async queries
- Implement position calculation efficiently (use COUNT + 1)
- Case-insensitive email lookups using `.ToLower()`
- Batch operations for analytics
- Pagination with proper ordering
- Add appropriate database indexes (see Section 7)

---

## 7. Database Changes

### 7.1 EF Core Migration

**File Name:** `20260618_AddWaitlistFeatures.cs` (or appropriate timestamp)

**Migration Content:**
1. Add columns to `Waitlists` table:
   - `status` (varchar(50)) DEFAULT 'Pending'
   - `position` (bigint) UNIQUE NOT NULL
   - `email_confirmed` (boolean) DEFAULT false
   - `email_confirmation_token` (text) nullable
   - `email_confirmed_at` (timestamp with time zone) nullable
   - `invitation_token` (text) nullable
   - `promoted_user_id` (uuid) nullable
   - `promoted_at` (timestamp with time zone) nullable
   - `notes` (text) nullable

2. Create indexes:
   ```sql
   -- Lookup indexes
   CREATE UNIQUE INDEX IX_Waitlists_Email ON Waitlists(LOWER(Email));
   CREATE INDEX IX_Waitlists_Status ON Waitlists(Status);
   CREATE INDEX IX_Waitlists_Position ON Waitlists(Position);
   
   -- Performance indexes
   CREATE INDEX IX_Waitlists_EmailConfirmed ON Waitlists(Status) 
       WHERE Status = 'EmailConfirmed';
   CREATE INDEX IX_Waitlists_CreatedAt ON Waitlists(CreatedAt DESC);
   
   -- FK index
   CREATE INDEX IX_Waitlists_PromotedUserId ON Waitlists(PromotedUserId);
   ```

3. Add foreign key (optional, for referential integrity):
   ```sql
   ALTER TABLE Waitlists 
   ADD CONSTRAINT FK_Waitlists_PromotedUserId 
   FOREIGN KEY (PromotedUserId) REFERENCES AspNetUsers(Id) 
   ON DELETE SET NULL;
   ```

### 7.2 EF Core Model Configuration

**In VulnWatchDbContext.OnModelCreating:**

```csharp
builder.Entity<Waitlist>(e =>
{
    e.HasKey(w => w.Id);
    
    // Columns
    e.Property(w => w.Email)
        .IsRequired()
        .HasMaxLength(254);
    
    e.Property(w => w.CompanyName)
        .HasMaxLength(200)
        .IsRequired(false);
    
    e.Property(w => w.Status)
        .HasConversion<string>()
        .HasMaxLength(50);
    
    e.Property(w => w.Position)
        .IsRequired();
    
    e.Property(w => w.EmailConfirmed)
        .HasDefaultValue(false);
    
    e.Property(w => w.EmailConfirmationToken)
        .HasMaxLength(500)
        .IsRequired(false);
    
    e.Property(w => w.InvitationToken)
        .HasMaxLength(500)
        .IsRequired(false);
    
    e.Property(w => w.Notes)
        .IsRequired(false);
    
    // Indexes
    e.HasIndex(w => w.Email).IsUnique();
    e.HasIndex(w => w.Status);
    e.HasIndex(w => w.Position);
    e.HasIndex(w => w.CreatedAt);
    e.HasIndex(w => w.PromotedUserId);
    
    // Foreign Key (if desired)
    // e.HasOne<User>()
    //     .WithMany()
    //     .HasForeignKey(w => w.PromotedUserId)
    //     .OnDelete(DeleteBehavior.SetNull);
    
    e.ToTable("Waitlists");
});
```

---

## 8. Controller

### 8.1 WaitlistController

**File:** `api-dotnet/src/Web/Controllers/WaitlistController.cs` (NEW)

**Structure:**

```csharp
[ApiController]
[Route("api/[controller]")]
public class WaitlistController : ControllerBase
{
    private readonly IMediator _mediator;

    // POST /api/waitlist/join
    [HttpPost("join")]
    [AllowAnonymous]
    [EnableRateLimiting("default")]
    public async Task<ActionResult<Result<WaitlistResponse>>> JoinWaitlist(
        JoinWaitlistRequest request, CancellationToken ct)

    // GET /api/waitlist/status
    [HttpGet("status")]
    [AllowAnonymous]
    [EnableRateLimiting("default")]
    public async Task<ActionResult<Result<WaitlistResponse>>> GetStatus(
        [FromQuery] string email, CancellationToken ct)

    // GET /api/waitlist/verify
    [HttpGet("verify")]
    [AllowAnonymous]
    [EnableRateLimiting("default")]
    public async Task<ActionResult<Result<MessageResponse>>> VerifyEmail(
        [FromQuery] string email, [FromQuery] string token, CancellationToken ct)

    // POST /api/waitlist/cancel
    [HttpPost("cancel")]
    [AllowAnonymous]
    [EnableRateLimiting("default")]
    public async Task<ActionResult<Result<MessageResponse>>> CancelWaitlist(
        CancelWaitlistRequest request, CancellationToken ct)
}
```

**Note:** All public endpoints should support rate limiting to prevent abuse.

---

## 9. Services & Utilities

### 9.1 Token Generation Service

**Modify:** `api-dotnet/src/Application/Interfaces/ITokenService.cs` (extend if needed)

**Or Create:** `api-dotnet/src/Application/Interfaces/IWaitlistTokenService.cs`

Required functionality:
- Generate confirmation token (URL-safe, expires in 7 days)
- Generate invitation token (URL-safe, expires in 30 days)
- Validate token + check expiry
- Hash tokens before storage (if using secure tokens)

### 9.2 Email Templates

**Location:** `api-dotnet/src/Web/Services/` or `api-dotnet/src/Infrastructure/Services/`

**Templates Needed:**
1. **WaitlistConfirmationEmail**
   - User email, position number, confirmation link
   
2. **WaitlistPositionUpdateEmail**
   - Current position update
   
3. **WaitlistPromotionEmail**
   - Congratulations on promotion to account
   - Password reset link or temporary login link
   - Instructions to complete profile

### 9.3 Position Calculation Strategy

**Options:**
1. **Eager** - Calculate position on every join (COUNT + 1)
   - Pro: Real-time accuracy
   - Con: Database hits, O(n) complexity as table grows
   
2. **Lazy with Background Job** - Recalculate positions periodically
   - Pro: Batch efficient
   - Con: Slight staleness acceptable
   
3. **Hybrid** - Cache position, refresh daily/weekly
   - Pro: Balance of accuracy and performance
   - Con: Cache management complexity

**Recommendation:** Start with Eager (simplicity), move to Batch if performance issue arises.

---

## 10. Validation Rules (FluentValidation)

### 10.1 JoinWaitlistValidator

```
Email:
  - NotEmpty
  - MaximumLength(254)
  - EmailAddress (proper RFC validation)
  
CompanyName:
  - MaximumLength(200) when provided
  - No leading/trailing whitespace
```

### 10.2 VerifyWaitlistEmailValidator

```
Email:
  - NotEmpty
  - EmailAddress
  
Token:
  - NotEmpty
  - MinimumLength(32)
```

### 10.3 PromoteWaitlistValidator (Admin)

```
WaitlistId:
  - NotEqual(Guid.Empty)
  
FirstName (optional):
  - MaximumLength(100) when provided
  
LastName (optional):
  - MaximumLength(100) when provided
```

---

## 11. Error Handling

### 11.1 Custom Errors

**Define in:** `api-dotnet/src/Domain/Common/Error.cs` (extend existing)

New error types needed:
```
WaitlistError.Conflict("Email already on waitlist", position?, status?)
WaitlistError.InvalidToken("Token expired or invalid")
WaitlistError.AlreadyConfirmed("Email already confirmed")
WaitlistError.NotConfirmed("Email not confirmed for promotion")
WaitlistError.AlreadyUser("Email is already a registered user")
```

---

## 12. Integration Points

### 12.1 Email Service Integration

**Existing:** `Application.Interfaces.IEmailService`

**Usage in Waitlist:**
- Send confirmation email on join
- Send position update email (if position changes significantly)
- Send promotion/invitation email on promotion
- Send cancellation email on cancellation

**Email Templates:** Use existing pattern (HTML body builder methods)

### 12.2 Current User Service

**Existing:** `Application.Interfaces.ICurrentUser`

**Usage:**
- NOT needed for public endpoints (no auth)
- Needed for admin endpoints (validate admin role)

**Admin Role Check:**
```csharp
if (!_currentUser.IsAdmin)
    return Result<T>.Failure(Error.Forbidden("Admin access required"));
```

### 12.3 JWT/Authentication

**No changes needed** - Use existing Identity/JWT setup in Program.cs

**Public endpoints:** `[AllowAnonymous]`
**Admin endpoints:** `[Authorize(Roles = "Admin")]` or custom policy

---

## 13. Testing Strategy

### 13.1 Unit Tests

**File Structure:**
```
api-dotnet/src/Tests/Application/Waitlist/
├── Commands/
│   ├── JoinWaitlistHandlerTests.cs
│   ├── VerifyWaitlistEmailHandlerTests.cs
│   ├── CancelWaitlistHandlerTests.cs
│   └── PromoteWaitlistHandlerTests.cs
├── Queries/
│   ├── GetWaitlistStatusHandlerTests.cs
│   ├── GetWaitlistListHandlerTests.cs
│   └── GetWaitlistAnalyticsHandlerTests.cs
└── Validators/
    ├── JoinWaitlistValidatorTests.cs
    └── VerifyWaitlistEmailValidatorTests.cs
```

**Test Scenarios:**

**JoinWaitlistHandler:**
- ✅ Valid email creates entry with Pending status
- ✅ CompanyName optional and properly stored
- ✅ Position auto-calculated correctly
- ✅ Duplicate email rejected (Conflict error)
- ❌ Existing User email rejected
- ❌ Invalid email format rejected
- ✅ Confirmation email sent
- ✅ Token generated and hashed

**VerifyWaitlistEmailHandler:**
- ✅ Valid token moves status to EmailConfirmed
- ❌ Invalid token rejected
- ❌ Expired token rejected
- ❌ Already confirmed status error

**PromoteWaitlistHandler:**
- ✅ User account created from waitlist
- ✅ Waitlist marked as Promoted
- ✅ PromotedUserId set correctly
- ✅ Invitation email sent
- ❌ Non-confirmed email rejected
- ❌ Already registered email rejected
- ✅ Transaction rolled back on error

**GetWaitlistListHandler:**
- ✅ Filters by status
- ✅ Pagination works correctly
- ✅ Sorting by position/createdAt
- ✅ Search by email prefix
- ❌ Non-admin rejected

### 13.2 Integration Tests

- ✅ POST /api/waitlist/join → GET /api/waitlist/status flow
- ✅ Email verification flow (token sent, verified, promoted)
- ✅ Admin bulk operations

### 13.3 API/Contract Tests

- ✅ Request validation (422 responses)
- ✅ Conflict responses (409)
- ✅ Not found responses (404)
- ✅ Rate limiting works

---

## 14. Security Considerations

### 14.1 Email Confirmation

- **Why:** Prevent spam entries and verify email ownership
- **How:** Time-limited token (7 days), sent via email
- **Token Format:** Generate random 32+ byte token, hash before storage (using standard hash)

### 14.2 Rate Limiting

- **Join endpoint:** 5 requests per IP per hour
- **Status check:** 10 requests per IP per hour
- **Verify endpoint:** 5 attempts per email per hour

**Configuration:** Use existing RateLimitExtensions pattern

### 14.3 Admin Promotion

- **Authorization:** Require [Authorize(Roles = "Admin")] or custom policy
- **Audit Trail:** Log who promoted which waitlist entry (log at INFO level)

### 14.4 Data Privacy

- No PII exposed in list endpoints without proper auth
- Email addresses should be treated as sensitive
- Consider GDPR right-to-be-forgotten (data deletion)

---

## 15. Future Enhancements

**Not in scope of initial implementation, but should be documented:**

1. **Segmentation** - Tag waitlist by industry/product interest
2. **Bulk Operations** - CSV import/export for admins
3. **Automated Promotion** - Promote by cohort/batch on schedule
4. **Communication Campaign** - Send position updates periodically
5. **Referral Tracking** - Track how users joined (source)
6. **Priority Tiers** - VIP waitlist tracks
7. **Waitlist Export** - Download as CSV/JSON for marketing
8. **A/B Testing** - Different email templates for cohorts

---

## 16. Implementation Checklist

### Phase 1: Domain & Infrastructure
- [ ] Update `Waitlist.cs` entity with new properties
- [ ] Create `WaitlistStatus.cs` enum
- [ ] Create EF Core migration
- [ ] Create `IWaitlistRepository` interface
- [ ] Implement `WaitlistRepository` class
- [ ] Add DbContext configuration
- [ ] Create database indexes

### Phase 2: Application Layer
- [ ] Create DTOs (Request/Response classes)
- [ ] Create Validators (FluentValidation)
- [ ] Create Queries (GetWaitlistStatus, GetWaitlistList, GetAnalytics)
- [ ] Create Query Handlers
- [ ] Create Commands (Join, Verify, Cancel, Promote, Update)
- [ ] Create Command Handlers
- [ ] Wire up MediatR registration (if not automatic)

### Phase 3: API Layer
- [ ] Create `WaitlistController`
- [ ] Implement all public endpoints
- [ ] Implement all admin endpoints
- [ ] Add XML documentation comments
- [ ] Add rate limiting attributes

### Phase 4: Services & Utilities
- [ ] Create/extend token generation service
- [ ] Create email templates for confirmations/invitations
- [ ] Implement email sending in handlers

### Phase 5: Testing
- [ ] Unit tests for handlers
- [ ] Unit tests for validators
- [ ] Integration tests for workflows
- [ ] API contract tests

### Phase 6: Documentation & Deployment
- [ ] Update API documentation (Swagger)
- [ ] Write admin guide (promotion workflow)
- [ ] Add monitoring/logging
- [ ] Deploy migration scripts
- [ ] QA testing

---

## 17. Dependencies

### Existing Libraries (No new dependencies needed)
- MediatR - Already used for CQRS pattern
- FluentValidation - Already used for validation
- EF Core - Already used for ORM
- AspNetCore - Already used for API

### Services Already Available
- IEmailService
- ITokenService (or similar)
- User authentication/authorization
- Logging (Serilog)

---

## 18. Configuration Requirements

**appsettings.json entries needed:**

```json
{
  "Waitlist": {
    "ConfirmationTokenExpiryDays": 7,
    "InvitationTokenExpiryDays": 30,
    "SendConfirmationEmail": true,
    "AdminApprovalRequired": false
  },
  "Email": {
    "WaitlistConfirmationTemplate": "waitlist-confirmation",
    "WaitlistInvitationTemplate": "waitlist-invitation"
  }
}
```

---

## 19. Monitoring & Observability

### Metrics to Track
- Total waitlist size (by day)
- New joiners (by day)
- Email confirmation rate (%)
- Promotion conversion rate (%)
- Time from join to promotion (avg)
- Cancellation rate (%)

### Logging Points
- Waitlist entry created (INFO)
- Email confirmation attempted (DEBUG)
- Admin promotion action (WARNING - for audit)
- Errors in handlers (ERROR)

### Health Checks
- Database connectivity
- Email service availability

---

## 20. Deployment Considerations

1. **Database Migration** - Run before deploying new code
2. **Backwards Compatibility** - No breaking changes to existing APIs
3. **Feature Flag** - Consider gating new endpoints behind a feature flag initially
4. **Monitoring** - Set up alerts for error rates in new endpoints
5. **Capacity Planning** - Email sending throughput for promotion emails

---

## Summary

This specification provides a complete, detailed blueprint for implementing a production-ready Waitlist feature for Vulnwatch. The design:

✅ **Follows existing patterns** - Uses MediatR, FluentValidation, EF Core like other features  
✅ **Includes security** - Email confirmation, rate limiting, admin authorization  
✅ **Is well-structured** - Clear separation of concerns (Domain → Application → Infrastructure → Web)  
✅ **Handles edge cases** - Duplicates, tokens, expiry, concurrency  
✅ **Supports analytics** - Admin dashboard with metrics  
✅ **Is testable** - Clear contracts and dependencies  
✅ **Is documented** - Every endpoint, handler, and requirement specified  

**Ready to begin implementation without any ambiguity about requirements.**

---

**Document prepared:** 2026-06-18  
**Ready for:** Development team assignment and implementation planning
