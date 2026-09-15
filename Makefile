TFM      := netstandard2.1
CONFIG   := Debug
DLL      := VGSilos.dll

BUILDDIR := VGSilos/bin/$(CONFIG)/$(TFM)
BUILDDLL := $(BUILDDIR)/$(DLL)

# WSL path to the game install — adjust if Steam lives elsewhere
GAME_DIR := /mnt/c/Program Files (x86)/Steam/steamapps/common/Vanguard Galaxy
PLUGIN_DIR := $(GAME_DIR)/BepInEx/plugins

# Resolve dotnet — prefer explicit local SDK, fall back to PATH
DOTNET   ?= $(shell command -v dotnet 2>/dev/null || echo /tmp/dnsdk/dotnet/dotnet)

.PHONY: all build link-asm link-api test clean deploy check-bepinex

all: build

check-bepinex:
	@test -d "$(GAME_DIR)/BepInEx/plugins" || { \
		echo "BepInEx plugins dir not found at $(GAME_DIR)/BepInEx/plugins." ; \
		echo "Install BepInEx 5.x into the game folder and launch the game once." ; \
		exit 1 ; \
	}

# Symlink the game's runtime DLLs into VGSilos/lib/ for compilation references.
# Mirrors the VGEcho pattern: the symlinks are local-only (gitignored), CI
# builds against publicized stubs that get committed separately.
link-asm:
	@mkdir -p VGSilos/lib
	@if [ ! -e "VGSilos/lib/Assembly-CSharp.dll" ]; then \
		ln -sf "$(GAME_DIR)/VanguardGalaxy_Data/Managed/Assembly-CSharp.dll" VGSilos/lib/Assembly-CSharp.dll ; \
		echo "Linked Assembly-CSharp.dll" ; \
	fi
	@if [ ! -e "VGSilos/lib/UnityEngine.UI.dll" ]; then \
		ln -sf "$(GAME_DIR)/VanguardGalaxy_Data/Managed/UnityEngine.UI.dll" VGSilos/lib/UnityEngine.UI.dll ; \
		echo "Linked UnityEngine.UI.dll" ; \
	fi
	@if [ ! -e "VGSilos/lib/Unity.TextMeshPro.dll" ]; then \
		ln -sf "$(GAME_DIR)/VanguardGalaxy_Data/Managed/Unity.TextMeshPro.dll" VGSilos/lib/Unity.TextMeshPro.dll ; \
		echo "Linked Unity.TextMeshPro.dll" ; \
	fi

build: link-asm link-api
	DOTNET_ROOT=$(dir $(DOTNET)) $(DOTNET) build VGSilos/VGSilos.csproj -c $(CONFIG)

# Symlink the VGModAPI contract assembly for the SaveData provider.
# Mirrors VGMissionJournal: build the sibling API repo first
# (`make build` there) — the reference is compile-only; VGModAPI.dll
# itself is deployed as its own plugin next to ours.
VGAPI_DLL ?= ../vanguard-galaxy-api/VGModAPI.Abstractions/bin/Release/netstandard2.1/VGModAPI.Abstractions.dll

link-api:
	@mkdir -p VGSilos/lib
	@if [ ! -e "VGSilos/lib/VGModAPI.Abstractions.dll" ]; then \
		if [ ! -f "$(VGAPI_DLL)" ]; then \
			echo "VGModAPI.Abstractions.dll not found at $(VGAPI_DLL)." ; \
			echo "Build the sibling API repo first: (cd ../vanguard-galaxy-api && make build)" ; \
			exit 1 ; \
		fi ; \
		ln -sf "$(shell cd ../vanguard-galaxy-api 2>/dev/null && pwd)/VGModAPI.Abstractions/bin/Release/netstandard2.1/VGModAPI.Abstractions.dll" VGSilos/lib/VGModAPI.Abstractions.dll ; \
		echo "Linked VGModAPI.Abstractions.dll" ; \
	fi

test: link-asm link-api
	DOTNET_ROOT=$(dir $(DOTNET)) $(DOTNET) test VGSilos.Tests/VGSilos.Tests.csproj

deploy: build check-bepinex
	@mkdir -p "$(PLUGIN_DIR)"
	# Exact shipped file-set gate: the plugin must be standalone (game 0.8.1
	# ships no Newtonsoft.Json.dll, so we carry our own) and must not leak
	# compile-only deps. Any unexpected DLL in the build output fails deploy.
	@unexpected=$$(ls "$(BUILDDIR)"/*.dll 2>/dev/null | xargs -r -n1 basename | grep -v -E '^(VGSilos|Newtonsoft\.Json)\.dll$$' || true); \
	if [ -n "$$unexpected" ]; then \
		echo "ERROR: unexpected DLLs in build output (compile-only deps leaked?):" ; \
		echo "$$unexpected" ; exit 1 ; \
	fi
	@test -f "$(BUILDDIR)/Newtonsoft.Json.dll" || { \
		echo "ERROR: $(BUILDDIR)/Newtonsoft.Json.dll missing — VGSilos must ship its own copy." ; exit 1 ; }
	cp "$(BUILDDLL)" "$(PLUGIN_DIR)/"
	cp "$(BUILDDIR)/Newtonsoft.Json.dll" "$(PLUGIN_DIR)/"
	@if [ -f "$(BUILDDIR)/VGSilos.pdb" ]; then cp "$(BUILDDIR)/VGSilos.pdb" "$(PLUGIN_DIR)/"; fi
	@echo "Deployed $(DLL) + Newtonsoft.Json.dll to $(PLUGIN_DIR)"

clean:
	$(DOTNET) clean VGSilos/VGSilos.csproj
	rm -rf VGSilos/bin VGSilos/obj
