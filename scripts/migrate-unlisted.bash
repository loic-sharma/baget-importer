#!/usr/bin/env bash

#Bash function to print usage
function usage() {
    echo "Migrate packages from one feed to another"
    echo "Usage: $0 <file> <source-feed> <destination-feed>"
    echo "Example: $0 packages.txt https://nuget.tradera.service https://nuget.tradera.dev"
    echo "Where packages is a file with one package per line in the format <id>|<version>"
    exit 1
}

function migrate_package() {
    local id=$1
    local version=$2
    local file="$id.$version.nupkg"
    local source_feed=$3
    local destination_feed=$4
    
    echo "Migrating $id $version"
    curl -s --output "$file" "$source_feed/v3/package/$id/$version/$file"  
    dotnet nuget push -s "$destination_feed" "$file" --skip-duplicate
    dotnet nuget delete -s "$destination_feed" "$id" "$version" --non-interactive
    rm "$file"
}

FILE=$1
if [ -z "$FILE" ]; then
    usage
fi

SOURCE_FEED="$2"
if [ -z "$SOURCE_FEED" ]; then
    usage
fi

DESTINATION_FEED=$3
if [ -z "$DESTINATION_FEED" ]; then
    usage
fi

echo "Migrating packages from $SOURCE_FEED to $DESTINATION_FEED"

export -f migrate_package
cat "$FILE" | parallel --colsep '\|' -j 100 --progress "migrate_package {1} {2} $SOURCE_FEED $DESTINATION_FEED"
