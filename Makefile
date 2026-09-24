.PHONY: demo bench test

# Precisa de Docker (Codespaces, CI ou local com Docker/Colima).
demo:
	dotnet run --project tools/Banking.Demo --configuration Release

bench:
	dotnet run --project tools/Banking.Demo --configuration Release -- --bench

test:
	dotnet test
