# Local secrets setup (Development)

This project uses ASP.NET Core User Secrets for sensitive runtime settings.

## Initialize and set values

From `BearTrap.Hackathon/BearTrap.Hackathon`:

```powershell
dotnet user-secrets set "ChainDataProvider" "Bitquery"
dotnet user-secrets set "Bitquery:Endpoint" "https://streaming.bitquery.io/graphql"
dotnet user-secrets set "Bitquery:Token" "<your-bitquery-token>"
dotnet user-secrets set "FourMeme:FactoryAddress" "0x5c952063c7fc8610ffdb798152d69f0b9550762b"
dotnet user-secrets set "BnbRpc:Url" "https://<your-rpc-endpoint>"
```

For RPC mode:

```powershell
dotnet user-secrets set "ChainDataProvider" "Rpc"
```

For mock/offline mode:

```powershell
dotnet user-secrets set "ChainDataProvider" "Offchain"
```

## Environment variables (alternative)

Use standard ASP.NET Core double-underscore syntax:

- `Bitquery__Endpoint`
- `Bitquery__Token`
- `FourMeme__FactoryAddress`
- `BnbRpc__Url`
- `ChainDataProvider`

## Behavior when values are missing

- App startup does not crash.
- If a provider is selected but required config is missing, requests fail with a clear configuration error message.
