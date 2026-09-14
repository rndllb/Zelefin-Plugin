.PHONY: test build zip deploy relay

VERSION := 1.0.0.0
FILE := zelefin-$(VERSION).zip

test:
	dotnet test
	dotnet build relay/Zelefin.PushRelay.csproj --nologo

build:
	dotnet build Jellyfin.Plugin.Zelefin --configuration Release

relay:
	dotnet build relay/Zelefin.PushRelay.csproj --configuration Release

zip: build
	mkdir -p dist
	cd Jellyfin.Plugin.Zelefin/bin/Release/net10.0 && zip -j "$(CURDIR)/dist/$(FILE)" Jellyfin.Plugin.Zelefin.dll

deploy:
	bash scripts/deploy-zimaos.sh
