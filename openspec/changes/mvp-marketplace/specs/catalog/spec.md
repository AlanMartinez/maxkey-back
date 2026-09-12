# Catalog Specification

## ADDED Requirements

### Requirement: Product Listing
The system MUST expose a public, unauthenticated endpoint that lists active products only, supporting platform filtering and text search.

#### Scenario: List active products
- GIVEN products exist with `IsActive=true` and `IsActive=false`
- WHEN a client requests the product list with no filters
- THEN only active products are returned

#### Scenario: Filter by platform
- GIVEN products for multiple platforms exist
- WHEN a client requests the list with `platform=PS5`
- THEN only products matching that platform are returned

#### Scenario: Search by name
- GIVEN a product named "FC Points"
- WHEN a client searches `q=FC`
- THEN the product is included in results

### Requirement: Product Variant Exposure
Every product MUST have at least one active `ProductVariant` (region/edition/tier), and the system MUST NOT display a stock counter to buyers.

#### Scenario: Variant list on product detail
- GIVEN a product with 3 variants
- WHEN a client requests the product detail by slug
- THEN all active variants are returned with their ARS price, region, and edition
- AND no quantity-in-stock field is present in the response

### Requirement: Product Detail Lookup
The system MUST resolve a product by slug and return 404 Problem Details when the product does not exist or is inactive.

#### Scenario: Unknown slug
- GIVEN no product has slug `does-not-exist`
- WHEN a client requests that product detail
- THEN the response is 404 with a Problem Details body

#### Scenario: Inactive product
- GIVEN a product exists but `IsActive=false`
- WHEN a client requests that product by slug
- THEN the response is 404

### Requirement: Image URL Resolution
The system MUST resolve stored R2 object keys into fully-qualified, publicly accessible image URLs in catalog responses.

#### Scenario: Image URL present
- GIVEN a product has `ImageKey=products/abc.png`
- WHEN the product is returned in any catalog response
- THEN the response includes a resolved absolute URL, not the raw key
