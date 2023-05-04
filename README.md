# BaGet Importer

Import your packages from a NuGet server to your BaGet instance:

1. Install the [.NET SDK](https://dotnet.microsoft.com/download)
1. Install [`baget-import`](https://www.nuget.org/packages/baget-import/): `dotnet tool install --global baget-import --version 1.0.0-preview2`
1. Run `baget-import --from-source https://api.nuget.org/v3/index.json --to-source http://my-baget-server.test/v3/index.json --api-key MY-BAGET-SERVER-API-KEY --batch-size 1000 --min-batch-interval 1000`


# Changes
This fork contains the ability to import packages in parallel batches controlled via `--batch-size <num>` and `--min-batch-interval <ms>`

## scripts/migrate-unlisted.bash
Help script for migrating unlisted packages

1. Get a list of unlisted packages `sqlite3 --list baget.db "SELECT Id, Version from Packages as p where p.Listed == false" > packages.list`
2. Run script `bash migrate-unlisted.bash packages.list http://baget-old.example.org https://baget-new.example.org`