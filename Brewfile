# Host developer/operator toolchain for cichlids.com, installed cleanly via Homebrew so it can be
# removed again without trace (brew uninstall <tool>, or remove Homebrew entirely). This is the
# install path for your own host; CI and the devcontainer install the same tools at exact pinned
# versions via tools/install-toolchain.sh (ephemeral environments, never your host).
#
# Homebrew installs the current stable of each formula. The minimum versions the project requires
# live in tools/toolchain.versions and are enforced by tools/verify-toolchain.sh, which the latest
# brew formulae satisfy.
#
# Install:  brew bundle
# Verify:   ./tools/verify-toolchain.sh core

brew "opentofu"       # provides `tofu`
brew "kubernetes-cli" # provides `kubectl`
brew "helm"
brew "kustomize"
brew "sops"
brew "age"
brew "conftest"
brew "kind"
brew "kubeconform"
brew "node"           # must be >= 24 (NODE_MIN); the current formula satisfies this
