# Carousel Specification

## Purpose

Replace the hardcoded `HeroCarousel.vue` slides with admin-managed `CarouselSlide` records, each linked to a catalog `Product`, exposed publicly in `SortOrder`.

## Requirements

### Requirement: Admin Slide Creation
The system MUST let an admin create a `CarouselSlide` referencing an existing `ProductId`, with `SortOrder`, `IsActive`, and optional `Title`/`Caption`/`ImageKey` overrides, restricted to `AdminPolicy`.

#### Scenario: Slide created for an existing product
- GIVEN an active product exists
- WHEN an admin creates a slide referencing that product with `SortOrder=1`
- THEN the slide is persisted and linked to the product

#### Scenario: Slide creation rejects unknown product
- GIVEN no product has the submitted `ProductId`
- WHEN an admin attempts to create a slide referencing it
- THEN the response is 422 Problem Details (domain validation) and no slide is created

### Requirement: Admin Slide Update and Removal
The system MUST let an admin update a slide's `SortOrder`, `IsActive`, and optional overrides, and delete a slide, restricted to `AdminPolicy`.

#### Scenario: Slide reordered
- GIVEN two slides with `SortOrder=1` and `SortOrder=2`
- WHEN an admin updates the second slide to `SortOrder=0`
- THEN the public listing returns it first

#### Scenario: Slide deleted
- GIVEN an existing slide
- WHEN an admin deletes it
- THEN it no longer appears in the admin listing or the public endpoint

### Requirement: Public Carousel Listing
The system MUST expose an unauthenticated `GET /catalog/carousel` returning slides ordered by `SortOrder` ascending, excluding slides where `IsActive=false` or the referenced product is inactive. Each returned slide MUST use its own `Title`/`Caption`/`ImageKey` override when present, falling back to the linked product's `Name`/`Description`/`ImageKey`.

#### Scenario: Inactive slide hidden
- GIVEN a slide with `IsActive=false`
- WHEN a client requests the public carousel
- THEN that slide is excluded from the response

#### Scenario: Slide hidden when its product is inactive
- GIVEN an active slide linked to a product with `IsActive=false`
- WHEN a client requests the public carousel
- THEN that slide is excluded from the response

#### Scenario: Override falls back to product fields
- GIVEN a slide with no `Title` override, linked to a product named "FC Points"
- WHEN a client requests the public carousel
- THEN the slide's returned title is the product's `Name`

### Requirement: Admin Carousel Authorization
Admin carousel endpoints MUST require `AdminPolicy`.

#### Scenario: Non-admin rejected
- GIVEN a valid JWT whose `sub` is not in `Auth:AdminSubs`
- WHEN that user calls an admin carousel endpoint
- THEN the response is 403
