# Waitlist Feature Implementation Guide

## Overview

The Waitlist feature has been fully implemented with a clean architecture approach following CQRS (Command Query Responsibility Segregation) pattern. This feature allows users to join a waitlist, verify their email, and admins to manage the waitlist through promotion to user accounts.

## Architecture

The implementation follows a 4-layer architecture:

### 1. **Domain Layer** (`src/Domain/`)
- **Entities**: `Waitlist.cs` - Core domain entity with business logic
- **Enums**: `WaitlistStatus.cs` - Defines lifecycle states (Pending, EmailConfirmed, Promoted, Cancelled)

### 2. **Application Layer** (`src/Application/Features/Waitlist/`)
- **Commands**: Implement user actions (Join, Verify, Cancel, Promote, Update, Delete)
- **Queries**: Retrieve information (GetStatus, GetList, GetAnalytics)
- **Validators**: Input validation using FluentValidation
- **DTOs**: Request/response data transfer objects

### 3. **Infrastructure Layer** (`src/Infrastructure/`)
- **Repository**: `WaitlistRepository.cs` - Database access implementation
- **Migrations**: EF Core migrations for schema management
- **DbContext Configuration**: Entity mapping and indexes

### 4. **Web Layer** (`src/Web/`)
- **Controller**: `WaitlistController.cs` - HTTP API endpoints
- **Middleware**: Error handling and response mapping

## Feature Specification

### Core Functionality

#### 1. Join Waitlist (Public)
**Endpoint**: `POST /api/waitlist/join`

**Request**:
```json
{
  "email": "user@example.com",
  "companyName": "Tech Corp"
}
```

**Response**:
```json
{
  "email": "user@example.com",
  "companyName": "Tech Corp",
  "position": 150,
  "status": "Pending",
  "emailConfirmed": false
}
```

**Business Logic**:
- Validates email format and uniqueness (case-insensitive)
- Prevents registration of already-registered users
- Generates secure confirmation token
- Assigns sequential position on waitlist
- Sends confirmation email with verification link

#### 2. Verify Email (Public)
**Endpoint**: `GET /api/waitlist/verify?email=user@example.com&token=...`

**Response**:
```json
{
  "success": true,
  "message": "Email verified successfully"
}
```

**Business Logic**:
- Validates confirmation token matches stored token
- Moves status from Pending → EmailConfirmed
- Clears confirmation token after use

#### 3. Check Waitlist Status (Public)
**Endpoint**: `GET /api/waitlist/status?email=user@example.com`

**Response**:
```json
{
  "status": "EmailConfirmed",
  "position": 150,
  "totalCount": 5000
}
```

**Business Logic**:
- Normalizes email casing
- Returns a generic status response and total waitlist size without revealing whether the email is on the waitlist

#### 4. Cancel Waitlist (Public)
**Endpoint**: `POST /api/waitlist/cancel`

**Request**:
```json
{
  "email": "user@example.com"
}
```

**Response**:
```json
{
  "success": true,
  "message": "Successfully cancelled waitlist entry"
}
```

**Business Logic**:
- Only allows cancellation if status is Pending or EmailConfirmed
- Prevents cancellation of promoted accounts
- Marks entry as Cancelled

#### 5. Promote to User (Admin)
**Endpoint**: `POST /api/waitlist/admin/{id}/promote`

**Query Parameters**:
```http
?firstName=John&lastName=Doe&sendInvitationEmail=true
```

**Response**:
```json
{
  "newUserId": "550e8400-e29b-41d4-a716-446655440000",
  "email": "user@example.com",
  "message": "User promoted successfully"
}
```

**Business Logic**:
- Only works for EmailConfirmed entries
- Creates new User account
- Confirms email on User
- Generates password reset token
- Sends invitation email
- Ensures atomicity (all-or-nothing)

#### 6. List Waitlist (Admin)
**Endpoint**: `GET /api/waitlist/admin/list?page=1&pageSize=20&status=EmailConfirmed&searchEmail=user&sortBy=position&sortOrder=asc`

**Response**:
```json
{
  "items": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "email": "user@example.com",
      "position": 150,
      "status": "EmailConfirmed",
      "companyName": "Tech Corp",
      "emailConfirmedAt": "2024-06-18T10:30:00Z",
      "createdAt": "2024-06-18T09:00:00Z"
    }
  ],
  "totalCount": 5000,
  "page": 1,
  "pageSize": 20,
  "totalPages": 250
}
```

**Business Logic**:
- Pagination: page ≥ 1, pageSize ≥ 1 and ≤ 500
- Filtering: By status or email (case-insensitive contains)
- Sorting: By position, email, status, or createdAt
- Default: Sort by position ascending

#### 7. Update Waitlist (Admin)
**Endpoint**: `PUT /api/waitlist/admin/{id}`

**Request**:
```json
{
  "companyName": "Updated Company",
  "notes": "Internal notes"
}
```

**Response**:
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "email": "user@example.com",
  "position": 150,
  "status": "Pending",
  "companyName": "Updated Company",
  "notes": "Internal notes"
}
```

**Business Logic**:
- All fields optional
- Prevents status changes for Promoted/Cancelled entries
- Updates only provided fields

#### 8. Delete Waitlist (Admin)
**Endpoint**: `DELETE /api/waitlist/admin/{id}`

**Response**: 204 No Content with no response body.

#### 9. Get Analytics (Admin)
**Endpoint**: `GET /api/waitlist/admin/analytics`

**Response**:
```json
{
  "totalCount": 5000,
  "pendingCount": 3000,
  "emailConfirmedCount": 1500,
  "promotedCount": 450,
  "cancelledCount": 50,
  "promotionRate": 9.0,
  "cancellationRate": 1.0,
  "averageDaysToPromotion": 45.5,
  "topCompanies": [
    {
      "companyName": "Tech Corp",
      "count": 250
    }
  ]
}
```

## Running the Application

### Prerequisites

1. **.NET 8 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
2. **PostgreSQL 13+** - [Download](https://www.postgresql.org/download/)
3. **Git**

### Setup Instructions

#### 1. Clone the Repository
```bash
git clone <repository-url>
cd api-dotnet
```

#### 2. Configure Database Connection
Edit `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "VulnWatchDb": "Server=localhost;Port=5432;Database=vulnwatch;User Id=postgres;Password=your_password;"
  },
  "FrontendUrl": {
    "Base": "http://localhost:3000",
    "WaitlistVerify": "http://localhost:3000/verify",
    "PasswordReset": "http://localhost:3000/reset-password"
  },
  "EmailService": {
    "ApiKey": "your_email_service_key",
    "FromEmail": "noreply@vulnwatch.com"
  }
}
```

#### 3. Apply Database Migrations
```bash
dotnet ef database update
```

This will create the `Waitlists` table with all necessary columns and indexes.

#### 4. Run the Application
```bash
dotnet run
```

The API will be available at `http://localhost:5000`

Swagger documentation is available at `http://localhost:5000/swagger`

## Database Schema

The migration creates the following table:

```sql
CREATE TABLE "Waitlists" (
  "Id" uuid NOT NULL PRIMARY KEY,
  "Email" varchar(254) NOT NULL UNIQUE,
  "CompanyName" varchar(200),
  "Status" varchar(50) NOT NULL DEFAULT 'Pending',
  "Position" bigint NOT NULL UNIQUE,
  "EmailConfirmed" boolean NOT NULL DEFAULT false,
  "EmailConfirmationToken" varchar(500),
  "EmailConfirmedAt" timestamp with time zone,
  "InvitationToken" varchar(500),
  "Notes" text,
  "PromotedUserId" uuid,
  "PromotedAt" timestamp with time zone,
  "CreatedAt" timestamp with time zone NOT NULL,
  "UpdatedAt" timestamp with time zone NOT NULL
);

-- Indexes
CREATE UNIQUE INDEX "IX_Waitlists_Email" ON "Waitlists"("Email");
CREATE INDEX "IX_Waitlists_Status" ON "Waitlists"("Status");
CREATE UNIQUE INDEX "IX_Waitlists_Position" ON "Waitlists"("Position");
CREATE INDEX "IX_Waitlists_CreatedAt" ON "Waitlists"("CreatedAt");
CREATE INDEX "IX_Waitlists_PromotedUserId" ON "Waitlists"("PromotedUserId");
```

## Testing

### Unit Tests
Unit tests are located in `src/Tests/Application/Waitlist/`:

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "TestClass=JoinWaitlistHandlerTests"

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverageFormat=cobertura
```

### Test Coverage
The feature includes unit tests for:
- **JoinWaitlistHandler**: Duplicate prevention, position calculation, email validation
- **VerifyWaitlistEmailHandler**: Token validation, status transitions
- **CancelWaitlistHandler**: Cancellation restrictions, status updates
- **GetWaitlistStatusHandler**: Public status queries

### Integration Tests
To test the full workflow:

1. **Join Waitlist**:
```bash
curl -X POST http://localhost:5000/api/waitlist/join \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@example.com",
    "companyName": "Test Company"
  }'
```

2. **Verify Email** (use token from email):
```bash
curl -X GET "http://localhost:5000/api/waitlist/verify?email=test@example.com&token=<token>"
```

3. **Check Status**:
```bash
curl -X GET "http://localhost:5000/api/waitlist/status?email=test@example.com"
```

4. **Admin List** (requires authentication):
```bash
curl -X GET "http://localhost:5000/api/waitlist/admin/list" \
  -H "Authorization: Bearer <jwt_token>"
```

## Email Templates

The feature sends two types of emails:

### 1. Email Confirmation
Sent when user joins waitlist.

**Template variables**:
- `ConfirmationUrl`: Link to verify email
- `Email`: User's email address
- `Position`: Current position on waitlist

### 2. Promotion Invitation
Sent when user is promoted to full account.

**Template variables**:
- `PasswordResetUrl`: Link to set password
- `Email`: User's email address
- `FirstName`, `LastName`: User's name

## Error Handling

All endpoints return standardized error responses:

```json
{
  "success": false,
  "error": {
    "code": "Validation",
    "message": "Email is required",
    "metadata": {
      "field": "email"
    }
  }
}
```

### Common Error Codes
- `Validation`: Input validation failed
- `NotFound`: Resource not found
- `Conflict`: Resource already exists or operation not allowed in current state
- `InternalServerError`: Unexpected server error

## Performance Considerations

1. **Indexes**: All critical columns are indexed (Email, Status, Position, CreatedAt, PromotedUserId)
2. **Pagination**: Supports up to 500 items per page for list queries
3. **Async/Await**: All database operations are asynchronous
4. **Query Optimization**: Repository uses compiled queries for frequently accessed data

## Security

1. **Email Tokens**: Cryptographically secure random tokens (32 bytes, Base64URL encoded)
2. **Authorization**: Admin endpoints require "Admin" role
3. **Rate Limiting**: All endpoints use ASP.NET Core rate limiting
4. **Input Validation**: FluentValidation for all requests
5. **Email Case-Insensitivity**: All email lookups convert to lowercase

## Future Enhancements

Potential improvements for future iterations:

1. **Batch Operations**: Promote multiple users at once
2. **Waitlist Rules**: Time-based promotions, tier-based selection
3. **Webhooks**: Notify external systems on status changes
4. **Export**: CSV export of waitlist data
5. **Analytics Dashboard**: Real-time metrics visualization
6. **Notifications**: SMS/push notifications for status changes

## Troubleshooting

### Migration Issues
```bash
# View pending migrations
dotnet ef migrations list

# Remove last migration (if applied)
dotnet ef migrations remove

# Reapply migrations
dotnet ef database update
```

### Build Issues
```bash
# Clean build
dotnet clean
dotnet build

# Restore packages
dotnet restore
```

### Database Connection Issues
```bash
# Test connection string
# In psql:
psql -U postgres -h localhost -d vulnwatch

# Check PostgreSQL is running
sudo systemctl status postgresql
```

## Support

For issues or questions:
1. Check existing GitHub issues
2. Create a new issue with detailed reproduction steps
3. Contact the development team at dev@vulnwatch.com

## License

See LICENSE file in project root.
