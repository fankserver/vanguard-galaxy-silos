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

.PHONY: all build link-asm clean deploy check-bepinex

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

build: link-asm
	DOTNET_ROOT=$(dir $(DOTNET)) $(DOTNET) build VGSilos/VGSilos.csproj -c $(CONFIG)

deploy: build check-bepinex
	@mkdir -p "$(PLUGIN_DIR)"
	cp "$(BUILDDLL)" "$(PLUGIN_DIR)/"
	@if [ -f "$(BUILDDIR)/VGSilos.pdb" ]; then cp "$(BUILDDIR)/VGSilos.pdb" "$(PLUGIN_DIR)/"; fi
	@echo "Deployed $(DLL) to $(PLUGIN_DIR)"

clean:
	$(DOTNET) clean VGSilos/VGSilos.csproj
	rm -rf VGSilos/bin VGSilos/obj
