# Waitlist Feature Verification Checklist

## Pre-Build Verification

- [ ] All source files are in correct locations:
  - [ ] `src/Domain/Entities/Waitlist.cs` exists
  - [ ] `src/Domain/Enums/WaitlistStatus.cs` exists
  - [ ] `src/Application/Features/Waitlist/` directory structure complete
  - [ ] `src/Infrastructure/Persistence/Repositories/WaitlistRepository.cs` exists
  - [ ] `src/Web/Controllers/WaitlistController.cs` exists

- [ ] Configuration files updated:
  - [ ] `src/Web/Program.cs` has IWaitlistRepository registration
  - [ ] `appsettings.json` has Email and FrontendUrl configuration

- [ ] Database setup:
  - [ ] PostgreSQL running on configured connection string
  - [ ] Migration file at `src/Infrastructure/Migrations/20260618_AddWaitlistFeatures.cs`

## Build Verification

```bash
cd api-dotnet
dotnet clean
dotnet build
```

- [ ] Build completes without errors
- [ ] No compiler warnings about Waitlist-related code
- [ ] All NuGet packages resolved correctly

## Database Migration Verification

```bash
dotnet ef database update
```

- [ ] Migration applies without errors
- [ ] PostgreSQL contains "Waitlists" table
- [ ] Table has all required columns:
  - [ ] Id (uuid, PK)
  - [ ] Email (varchar(254), unique)
  - [ ] CompanyName (varchar(200), nullable)
  - [ ] Status (varchar(50), default 'Pending')
  - [ ] Position (bigint, unique)
  - [ ] EmailConfirmed (boolean, default false)
  - [ ] EmailConfirmationToken (varchar(500), nullable)
  - [ ] EmailConfirmedAt (timestamp, nullable)
  - [ ] InvitationToken (varchar(500), nullable)
  - [ ] Notes (text, nullable)
  - [ ] PromotedUserId (uuid, nullable)
  - [ ] PromotedAt (timestamp, nullable)
  - [ ] CreatedAt (timestamp)
  - [ ] UpdatedAt (timestamp)

- [ ] All indexes created:
  - [ ] IX_Waitlists_Email (unique on Email)
  - [ ] IX_Waitlists_Status (on Status)
  - [ ] IX_Waitlists_Position (unique on Position)
  - [ ] IX_Waitlists_CreatedAt (on CreatedAt)
  - [ ] IX_Waitlists_PromotedUserId (on PromotedUserId)

## Unit Test Verification

```bash
dotnet test --filter "Waitlist"
```

- [ ] JoinWaitlistHandlerTests
  - [ ] Handle_WithValidEmail_CreatesWaitlistEntry passes
  - [ ] Handle_WithDuplicateEmail_ReturnsGenericSuccess passes
  - [ ] Handle_WithRegisteredUser_ReturnsGenericSuccess passes
  - [ ] Handle_EmailServiceFails_ReturnsValidationAndDoesNotSave passes

- [ ] VerifyWaitlistEmailHandlerTests
  - [ ] Handle_WithValidToken_ConfirmsEmail passes
  - [ ] Handle_WithInvalidToken_ReturnsBadRequest passes
  - [ ] Handle_WithNonExistentEmail_ReturnsNotFound passes
  - [ ] Handle_WithAlreadyConfirmedEmail_ReturnsBadRequest passes

- [ ] CancelWaitlistHandlerTests
  - [ ] Handle_WithValidEntry_CancelsSuccessfully passes
  - [ ] Handle_WithNonExistentEntry_ReturnsNotFound passes
  - [ ] Handle_WithAlreadyCancelledEntry_ReturnsBadRequest passes
  - [ ] Handle_WithPromotedEntry_ReturnsBadRequest passes
  - [ ] Handle_CaseInsensitiveEmail passes

- [ ] GetWaitlistStatusHandlerTests
  - [ ] Handle_WithAnyEmail_ReturnsGenericStatus passes
  - [ ] Handle_WithNonExistentEmail_ReturnsGenericSuccess passes
  - [ ] Handle_NormalizesEmail passes
  - [ ] Handle_DoesNotRevealStoredStatus passes

- [ ] PromoteWaitlistHandlerTests
  - [ ] Handle_WithConfirmedWaitlist_ConfirmsUserEmailAndSendsPasswordSetupInvite passes
  - [ ] Handle_WithSendInvitationEmailFalse_ConfirmsUserEmailWithoutSendingPasswordSetupInvite passes
  - [ ] Handle_WhenEmailConfirmationFails_RollsBackUserAndDoesNotPromote passes
  - [ ] Handle_WhenInvitationEmailFails_RollsBackUserAndDoesNotPromote passes

## Integration Test Verification

### Endpoint: POST /api/waitlist/join

```bash
curl -X POST http://localhost:5000/api/waitlist/join \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@example.com",
    "companyName": "Test Company"
  }'
```

Expected: 200 OK with WaitlistResponse
- [ ] Returns position ≥ 1
- [ ] Status is "Pending"
- [ ] EmailConfirmed is false
- [ ] Email validation works (reject invalid emails)
- [ ] Duplicate email returns generic success without creating a new entry
- [ ] CompanyName is optional

### Endpoint: GET /api/waitlist/status

```bash
curl "http://localhost:5000/api/waitlist/status?email=test@example.com"
```

Expected: 200 OK with WaitlistStatusResponse
- [ ] Returns generic position value without revealing membership
- [ ] Returns total count of waitlist
- [ ] Returns generic Pending status without revealing stored status
- [ ] Email normalization works
  - [ ] Join with `Test@Example.com`, then query `GET /api/waitlist/status?email=test@example.com`
  - [ ] Response returns normalized email `test@example.com` without revealing whether the entry exists

### Endpoint: GET /api/waitlist/verify

```bash
# Get token from email confirmation email
curl "http://localhost:5000/api/waitlist/verify?email=test@example.com&token=<token>"
```

Expected: 200 OK
- [ ] Token validation works
- [ ] Status changes from Pending → EmailConfirmed
- [ ] Invalid token rejected
- [ ] Token cleared after verification

### Endpoint: POST /api/waitlist/cancel

```bash
curl -X POST http://localhost:5000/api/waitlist/cancel \
  -H "Content-Type: application/json" \
  -d '{"email": "test@example.com", "token": "<valid-cancellation-token>"}'
```

Expected: 200 OK
- [ ] Pending entries can be cancelled
- [ ] EmailConfirmed entries can be cancelled
- [ ] Promoted entries cannot be cancelled
- [ ] Already cancelled entries return error
- [ ] Case-insensitive cancellation works
  - [ ] Join with `Test@Example.com`
  - [ ] Generate/use a valid cancellation token for that waitlist entry
  - [ ] Cancel with body `{"email":"test@example.com","token":"<valid-cancellation-token>"}`
  - [ ] Same entry is cancelled

### Endpoint: GET /api/waitlist/admin/list (Admin Only)

```bash
curl -X GET "http://localhost:5000/api/waitlist/admin/list?page=1&pageSize=10" \
  -H "Authorization: Bearer <jwt_token>"
```

Expected: 200 OK with PagedResult
- [ ] Pagination works (page, pageSize)
- [ ] Filtering by status works
- [ ] Filtering by email (contains) works
- [ ] Sorting by position/email/status/createdAt works
- [ ] TotalCount is correct
- [ ] Authorization required (401 without token)

### Endpoint: POST /api/waitlist/admin/{id}/promote (Admin Only)

```bash
curl -X POST "http://localhost:5000/api/waitlist/admin/{id}/promote?firstName=John&lastName=Doe&sendInvitationEmail=true" \
  -H "Authorization: Bearer <jwt_token>"
```

Expected: 200 OK with PromoteWaitlistResponse
- [ ] Creates new User account
- [ ] Sets status to Promoted
- [ ] Only works for EmailConfirmed entries
- [ ] User cannot be created if email already registered
- [ ] Password reset email sent (verify in email service logs)
- [ ] PromotedUserId matches new User ID
- [ ] Authorization required

### Endpoint: PUT /api/waitlist/admin/{id} (Admin Only)

```bash
curl -X PUT "http://localhost:5000/api/waitlist/admin/{id}" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <jwt_token>" \
  -d '{
    "companyName": "Updated Company",
    "notes": "Updated notes"
  }'
```

Expected: 200 OK with updated entry
- [ ] CompanyName updates
- [ ] Notes updates
- [ ] Status can be updated (with restrictions)
- [ ] Promoted/Cancelled status changes blocked
- [ ] Authorization required

### Endpoint: DELETE /api/waitlist/admin/{id} (Admin Only)

```bash
curl -X DELETE "http://localhost:5000/api/waitlist/admin/{id}" \
  -H "Authorization: Bearer <jwt_token>"
```

Expected: 204 No Content
- [ ] Entry deleted from database
- [ ] Subsequent queries return NotFound
- [ ] Authorization required

### Endpoint: GET /api/waitlist/admin/analytics (Admin Only)

```bash
curl "http://localhost:5000/api/waitlist/admin/analytics" \
  -H "Authorization: Bearer <jwt_token>"
```

Expected: 200 OK with WaitlistAnalyticsDto
- [ ] totalCount calculated correctly
- [ ] Count by status accurate
- [ ] Promotion rate calculated correctly (promoted/total * 100)
- [ ] Cancellation rate calculated correctly
- [ ] Average days to promotion calculated (from Created → Promoted)
- [ ] Top companies returns list sorted by count descending
- [ ] Authorization required

## API Documentation Verification

- [ ] Swagger available at `/swagger`
- [ ] All 9 endpoints documented
- [ ] Request/response schemas correct
- [ ] Authorization requirements documented
- [ ] Rate limiting documented

## Error Handling Verification

Test error scenarios:
- [ ] Invalid email format returns 400 with Validation error
- [ ] Missing required fields return 400
- [ ] NotFound scenarios return 404
- [ ] Conflict scenarios (duplicate, invalid state) return 409
- [ ] Unauthorized access returns 401
- [ ] Server errors return 500

## Performance Verification

```bash
# Load test with 100 requests
for i in {1..100}; do
  curl -X POST http://localhost:5000/api/waitlist/join \
    -H "Content-Type: application/json" \
    -d "{\"email\": \"test$i@example.com\", \"companyName\": \"Company $i\"}"
done
```

- [ ] Response times < 200ms for typical requests
- [ ] No connection pool exhaustion
- [ ] Database queries use indexes
- [ ] No N+1 query issues

## Security Verification

- [ ] JWT token required for admin endpoints
- [ ] Role "Admin" required for /admin/* endpoints
- [ ] Email tokens are cryptographically random (32 bytes)
- [ ] Email tokens are URL-safe Base64 encoded
- [ ] Tokens expire/clear after use
- [ ] Rate limiting active on all endpoints
- [ ] Input validation prevents SQL injection
- [ ] Case-insensitive email handling prevents duplicate emails with different cases

## Email Service Verification

How to verify: run the API against a test SMTP inbox such as Mailhog (`localhost:1025`) or Mailtrap, perform the waitlist action, then inspect the captured message subject, recipient, body, and links before clicking them.

- [ ] Confirmation email sent when user joins
  - [ ] Subject is `Confirm Your Email - Vulnwatch Waitlist`
  - [ ] Contains position on waitlist
  - [ ] Contains verification link with query parameters, e.g. `<FrontendUrl:WaitlistVerify>/?email=test%40example.com&token=<token>`
  - [ ] Contains email address
  - [ ] Uses configured FromEmail

- [ ] Invitation email sent when promoted
  - [ ] Subject is `Welcome to Vulnwatch - Set Your Password`
  - [ ] Contains password reset link with query parameters, e.g. `<FrontendUrl:PasswordReset>/?email=test%40example.com&token=<encoded-reset-token>`
  - [ ] Contains welcome and set-password instructions
  - [ ] Uses configured FromEmail
  - [ ] Link includes valid password reset token

## Documentation Verification

- [ ] WAITLIST_FEATURE_IMPLEMENTATION.md exists and is complete
- [ ] Feature overview section complete
- [ ] Architecture section describes all layers
- [ ] All 9 endpoints documented with examples
- [ ] Database schema documented
- [ ] Test instructions provided
- [ ] Troubleshooting section provided
- [ ] README updated (if applicable)

## Rollback Verification

```bash
# Test rollback capability
dotnet ef database update --migration <previous_migration>
```

- [ ] Migration down method removes all Waitlist tables and indexes
- [ ] Database is clean after rollback
- [ ] Can reapply migration without errors

## Final Checklist

- [ ] Code compiles without warnings
- [ ] All tests pass
- [ ] Database schema correct
- [ ] All endpoints functional
- [ ] Error handling works
- [ ] Security measures in place
- [ ] Documentation complete
- [ ] Ready for production deployment

## Sign-Off

- **Tested By**: _______________
- **Date**: _______________
- **Notes**: _______________
