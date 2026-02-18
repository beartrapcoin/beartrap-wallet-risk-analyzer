# Application Models

This folder contains application-level data models and contracts.

## Data Flow Summary

### IChainDataSource Contract

The `IChainDataSource` interface (located in `Application/Abstractions/`) defines the contract for unified chain data access.

**Key Methods:**
- `GetLatestTokensAsync(int count, CancellationToken ct)` → `IReadOnlyList<LatestToken>`
- `IsTokenFromFourMemeAsync(string tokenAddress, CancellationToken ct)` → `bool`

**Implementations:**
1. **BitqueryChainDataSource** (Infrastructure)
   - Maps Bitquery GraphQL DTOs to `LatestToken` (Domain model)
   - Internal DTOs: `GraphqlRequest`, `GraphqlResponse`, `GraphqlError`, `GraphqlData`, `EvmSection`, `EventRow`, `BlockInfo`, `TransactionInfo`, `EventArgument`, `ArgValue`
   - DTOs are sealed, private, and confined to Infrastructure

2. **RpcChainDataSource** (Infrastructure)
   - Implements same contract, currently stubbed
   - Will map RPC DTOs to `LatestToken` when implemented
   - Uses infrastructure DTOs: `LogEvent`, `LogFilter`, `TransactionReceipt` (defined in `Infrastructure/Rpc/`)

### Data Model Contract

Both implementations return identical types:
- **LatestToken** (Domain model)
  - Address: string
  - Name: string
  - Symbol: string
  - Creator: string
  - CreatedAt: DateTimeOffset
  - ImageUrl: string? (optional)

### Architecture Principles

✓ **No DTO Leakage**: Infrastructure DTOs (Bitquery, RPC) never cross layer boundaries
✓ **Unified Interface**: Both providers implement identical contract
✓ **Domain Model Contracts**: Services receive strongly-typed `LatestToken` regardless of provider
✓ **Internal Mapping**: Each provider handles its own DTO → LatestToken mapping

### Consumer Examples

- **RiskAnalyzer**: Receives `LatestToken` via `IFourMemeSource`
- **BitqueryFourMemeSource**: Delegates to `IChainDataSource`, returns `LatestToken`
- **Services**: All depend on unified contract, no provider-specific code required
