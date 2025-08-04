# .NET SDK Requirements

## Issue Summary
This repository requires .NET 9.0 SDK to build and run the projects successfully.

## Root Cause
- All C# projects target .NET 9.0 (`<TargetFramework>net9.0</TargetFramework>`)
- QuantConnect.Lean 2.5.* packages only support .NET 9.0 framework
- The code uses features from QuantConnect.Lean 2.5.* that are not available in older versions

## Current Error
If you try to build with .NET SDK 8.0, you'll see:
```
error NETSDK1045: The current .NET SDK does not support targeting .NET 9.0. Either target .NET 8.0 or lower, or use a version of the .NET SDK that supports .NET 9.0.
```

## Solution
Update your development environment and CI/CD pipeline to use .NET 9.0 SDK:

### Local Development
1. Download and install .NET 9.0 SDK from: https://dotnet.microsoft.com/download/dotnet/9.0
2. Verify installation: `dotnet --version` should show 9.0.x

### Docker/CI Environment
Update your Dockerfile or CI configuration to use a .NET 9.0 base image:
- For Docker: Use `mcr.microsoft.com/dotnet/sdk:9.0` as base image
- For GitHub Actions: Use `dotnet-version: '9.0.x'` in setup-dotnet action
- For other CI systems: Update to use .NET 9.0 SDK

### Verification
After updating the SDK, you should be able to build successfully:
```bash
cd arithmax-strategies/DiversifiedLeverage
dotnet build
```

## Why Not Downgrade?
Downgrading to .NET 8.0 is not feasible because:
1. QuantConnect.Lean 2.5.* only supports .NET 9.0
2. The code uses newer QuantConnect features not available in 2.4.* versions
3. Significant refactoring would be required to use older package versions