# Auth Specification

## ADDED Requirements

### Requirement: Configurable JWT Validation Mode
The system MUST validate incoming Supabase JWTs using JWKS as the primary mode and HS256 as a fallback, selected via the `Auth:Mode` configuration setting.

#### Scenario: JWKS mode validates a signed token
- GIVEN `Auth:Mode=Jwks` and a token signed with a key published in Supabase's JWKS endpoint
- WHEN a request presents that token
- THEN the token is accepted and claims (`sub`, email) are extracted

#### Scenario: HS256 mode validates a signed token
- GIVEN `Auth:Mode=Hs256` and a token signed with the configured shared secret
- WHEN a request presents that token
- THEN the token is accepted

#### Scenario: Invalid or expired token rejected
- GIVEN a token that is expired, malformed, or signed with an unknown key
- WHEN a request presents it to a protected endpoint
- THEN the response is 401 Problem Details

### Requirement: Anonymous Access to Guest-Eligible Endpoints
Checkout endpoints MUST NOT require authentication; user-scoped endpoints (`/me/*`) MUST require a valid JWT.

#### Scenario: Anonymous checkout allowed
- GIVEN no Authorization header
- WHEN a request is made to the checkout endpoint
- THEN the request proceeds without a 401

#### Scenario: Anonymous access to /me/orders rejected
- GIVEN no Authorization header
- WHEN a request is made to `/me/orders`
- THEN the response is 401

### Requirement: Admin Authorization Policy
The system MUST authorize admin endpoints only when the caller presents a valid JWT AND the token's `sub` is present in a configured admin allowlist. An empty allowlist MUST fail closed, denying all callers with 403.

#### Scenario: Authenticated non-admin denied
- GIVEN a valid JWT whose `sub` is not in the allowlist
- WHEN the caller requests an admin endpoint
- THEN the response is 403

#### Scenario: Empty allowlist denies everyone
- GIVEN the admin allowlist is empty
- WHEN any authenticated caller requests an admin endpoint
- THEN the response is 403

### Requirement: User Identity Linking
When a valid JWT is present, the system MUST use the token's `sub` as `Order.UserId` regardless of the buyer email submitted in the request.

#### Scenario: UserId persists independent of submitted email
- GIVEN an authenticated user with `sub=user-123`
- WHEN that user checks out with any email value
- THEN the resulting order has `UserId=user-123`
