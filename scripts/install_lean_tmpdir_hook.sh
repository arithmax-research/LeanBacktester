#!/usr/bin/env bash
#
# Installs (or removes) conda activation hooks that point $TMPDIR at a directory
# Docker can bind-mount, so plain `lean ...` commands work with Docker Desktop.
#
# Background: the Python LEAN CLI creates the files it bind-mounts
# (`Directory.Build.props`, `copy_csharp_dependencies.py`, `lean-cli-start.sh`)
# in `$TMPDIR`, i.e. `/tmp/lean-cli-*`. Docker Desktop only shares paths below
# `$HOME`, so container creation fails with:
#
#   Error: 400 Client Error ... Bad Request ("invalid mount config for type
#   "bind": bind source path does not exist: /tmp/lean-cli-XXXX/Directory.Build.props")
#
# Usage:
#   scripts/install_lean_tmpdir_hook.sh                 # install in $CONDA_PREFIX
#   scripts/install_lean_tmpdir_hook.sh --prefix DIR    # install in DIR
#   scripts/install_lean_tmpdir_hook.sh --tmpdir DIR    # custom temp directory
#   scripts/install_lean_tmpdir_hook.sh --uninstall     # remove the hooks
#
# Re-activate the environment afterwards (`conda deactivate && conda activate ...`)
# for the change to take effect in the current shell.

set -euo pipefail

marker="# Managed by LeanBacktester/scripts/install_lean_tmpdir_hook.sh"

usage() {
  cat <<'EOF'
Usage: install_lean_tmpdir_hook.sh [options]

Options:
  --prefix DIR     Conda environment to install into (default: $CONDA_PREFIX)
  --tmpdir DIR     Temporary directory to use (default: $HOME/.cache/lean-cli/tmp)
  --uninstall      Remove the hooks instead of installing them
  -h, --help       Show this help
EOF
}

prefix="${CONDA_PREFIX:-}"
tmpdir="${LEAN_CLI_TMPDIR:-${XDG_CACHE_HOME:-${HOME}/.cache}/lean-cli/tmp}"
uninstall=false

while [ $# -gt 0 ]; do
  case "$1" in
    --prefix)
      prefix="${2:?--prefix requires a value}"
      shift 2
      ;;
    --tmpdir)
      tmpdir="${2:?--tmpdir requires a value}"
      shift 2
      ;;
    --uninstall)
      uninstall=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "error: unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [ -z "$prefix" ]; then
  echo "error: no active conda environment detected; pass --prefix <env path>." >&2
  exit 1
fi

activate_hook="${prefix}/etc/conda/activate.d/00-lean-cli-tmpdir.sh"
deactivate_hook="${prefix}/etc/conda/deactivate.d/00-lean-cli-tmpdir.sh"

remove_hook() {
  local hook="$1"
  if [ ! -f "$hook" ]; then
    return 0
  fi
  if ! head -n 1 "$hook" | grep -Fq "$marker"; then
    echo "skipped: $hook was not created by this script, leaving it alone." >&2
    return 0
  fi
  rm -f "$hook"
  echo "removed: $hook"
}

if [ "$uninstall" = true ]; then
  remove_hook "$activate_hook"
  remove_hook "$deactivate_hook"
  echo "Re-activate the environment to restore the previous TMPDIR value."
  exit 0
fi

case "$tmpdir" in
  /*) ;;
  *)
    echo "error: the temp directory must be an absolute path: $tmpdir" >&2
    exit 1
    ;;
esac

# The path is written into shell hooks, so keep it to characters that never need
# escaping there.
case "$tmpdir" in
  *[!A-Za-z0-9_./-]*)
    echo "error: unsupported characters in temp directory: $tmpdir" >&2
    exit 1
    ;;
esac

mkdir -p "$tmpdir" "${prefix}/etc/conda/activate.d" "${prefix}/etc/conda/deactivate.d"

cat > "$activate_hook" <<EOF
${marker}
# Docker Desktop only shares paths below \$HOME with its VM, while the LEAN CLI
# creates the files it bind-mounts (Directory.Build.props, lean-cli-start.sh, ...)
# in \$TMPDIR, which defaults to /tmp/lean-cli-*. Keeping \$TMPDIR below \$HOME
# avoids: "bind source path does not exist: /tmp/lean-cli-XXXX/Directory.Build.props"
# Remove with: scripts/install_lean_tmpdir_hook.sh --uninstall
_LEAN_CLI_PREVIOUS_TMPDIR="\${TMPDIR-}"
export _LEAN_CLI_PREVIOUS_TMPDIR
TMPDIR="\${LEAN_CLI_TMPDIR:-${tmpdir}}"
mkdir -p "\${TMPDIR}" 2>/dev/null || :
export TMPDIR
EOF

cat > "$deactivate_hook" <<EOF
${marker}
if [ -n "\${_LEAN_CLI_PREVIOUS_TMPDIR+set}" ]; then
    if [ -n "\${_LEAN_CLI_PREVIOUS_TMPDIR}" ]; then
        TMPDIR="\${_LEAN_CLI_PREVIOUS_TMPDIR}"
        export TMPDIR
    else
        unset TMPDIR
    fi
    unset _LEAN_CLI_PREVIOUS_TMPDIR
fi
EOF

echo "installed: $activate_hook"
echo "installed: $deactivate_hook"
echo "TMPDIR will be set to: $tmpdir"
echo "Re-activate the environment to apply it: conda deactivate && conda activate \"\${CONDA_DEFAULT_ENV:-<env>}\""
