# BaGet Importer

Import your packages from a NuGet server to your BaGet instance:

1. Install the [.NET SDK](https://dotnet.microsoft.com/download)
1. Install [`baget-import`](https://www.nuget.org/packages/baget-import/): `dotnet tool install --global baget-import --version 1.0.0-preview1`
1. Run `baget-import --from-source https://api.nuget.org/v3/index.json --to-source http://my-baget-server.test/v3/index.json --api-key MY-BAGET-SERVER-API-KEY`