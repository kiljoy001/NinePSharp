#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_DIR=""

cleanup() {
  if [[ -n "${TMP_DIR}" && -d "${TMP_DIR}" ]]; then
    rm -rf "${TMP_DIR}"
  fi
}
trap cleanup EXIT

if [[ $# -gt 1 ]]; then
  echo "usage: $0 [path-to-diod-source-root]" >&2
  exit 2
fi

if [[ $# -eq 1 ]]; then
  DIOD_DIR="$1"
else
  TMP_DIR="$(mktemp -d)"
  TAR_PATH="${TMP_DIR}/diod_1.0.24.orig.tar.gz"
  curl -fsSL "https://deb.debian.org/debian/pool/main/d/diod/diod_1.0.24.orig.tar.gz" -o "${TAR_PATH}"
  tar -xzf "${TAR_PATH}" -C "${TMP_DIR}"
  DIOD_DIR="${TMP_DIR}/diod-1.0.24"
fi

if [[ ! -d "${DIOD_DIR}" ]]; then
  echo "diod source directory not found: ${DIOD_DIR}" >&2
  exit 2
fi

WORK_DIR="${TMP_DIR:-$(mktemp -d)}"
if [[ -z "${TMP_DIR}" ]]; then
  TMP_DIR="${WORK_DIR}"
fi

DIOD_LINES="${WORK_DIR}/diod_norm_lines.txt"
LOCAL_LINES="${WORK_DIR}/local_norm_lines.txt"
SHARED_LINES="${WORK_DIR}/shared_norm_lines.txt"
DIOD_WORDS="${WORK_DIR}/diod_words.txt"
LOCAL_WORDS="${WORK_DIR}/local_words.txt"
DIOD_10G="${WORK_DIR}/diod_10grams.txt"
LOCAL_10G="${WORK_DIR}/local_10grams.txt"
SHARED_10G="${WORK_DIR}/shared_10grams.txt"

find "${DIOD_DIR}" -type f \( -name '*.c' -o -name '*.h' -o -name '*.txt' \) -print0 \
  | xargs -0 cat \
  | tr '[:upper:]' '[:lower:]' \
  | sed 's/[^a-z0-9]/ /g' \
  | tr -s ' ' \
  | sed 's/^ //; s/ $//' \
  | awk 'length($0)>=40' \
  | sort -u > "${DIOD_LINES}"

find "${ROOT_DIR}/NinePSharp" "${ROOT_DIR}/NinePSharp.Parser" "${ROOT_DIR}/NinePSharp.Server" \
  -type f \( -name '*.cs' -o -name '*.fs' \) -print0 \
  | xargs -0 cat \
  | tr '[:upper:]' '[:lower:]' \
  | sed 's/[^a-z0-9]/ /g' \
  | tr -s ' ' \
  | sed 's/^ //; s/ $//' \
  | awk 'length($0)>=40' \
  | sort -u > "${LOCAL_LINES}"

comm -12 "${DIOD_LINES}" "${LOCAL_LINES}" > "${SHARED_LINES}"

find "${DIOD_DIR}" -type f \( -name '*.c' -o -name '*.h' \) -print0 \
  | xargs -0 cat \
  | tr '[:upper:]' '[:lower:]' \
  | sed 's/[^a-z0-9]/ /g' \
  | tr -s ' ' '\n' \
  | awk 'length($0)>=2' > "${DIOD_WORDS}"

find "${ROOT_DIR}/NinePSharp" "${ROOT_DIR}/NinePSharp.Parser" "${ROOT_DIR}/NinePSharp.Server" \
  -type f \( -name '*.cs' -o -name '*.fs' \) -print0 \
  | xargs -0 cat \
  | tr '[:upper:]' '[:lower:]' \
  | sed 's/[^a-z0-9]/ /g' \
  | tr -s ' ' '\n' \
  | awk 'length($0)>=2' > "${LOCAL_WORDS}"

awk 'NR>=10{print a1" "a2" "a3" "a4" "a5" "a6" "a7" "a8" "a9" "$0}{a1=a2;a2=a3;a3=a4;a4=a5;a5=a6;a6=a7;a7=a8;a8=a9;a9=$0}' "${DIOD_WORDS}" | sort -u > "${DIOD_10G}"
awk 'NR>=10{print a1" "a2" "a3" "a4" "a5" "a6" "a7" "a8" "a9" "$0}{a1=a2;a2=a3;a3=a4;a4=a5;a5=a6;a6=a7;a7=a8;a8=a9;a9=$0}' "${LOCAL_WORDS}" | sort -u > "${LOCAL_10G}"

comm -12 "${DIOD_10G}" "${LOCAL_10G}" > "${SHARED_10G}"

LINE_MATCHES="$(wc -l < "${SHARED_LINES}")"
NGRAM_MATCHES="$(wc -l < "${SHARED_10G}")"

echo "diod source: ${DIOD_DIR}"
echo "shared normalized lines (>=40 chars): ${LINE_MATCHES}"
echo "shared normalized 10-grams: ${NGRAM_MATCHES}"

if [[ "${LINE_MATCHES}" -gt 0 || "${NGRAM_MATCHES}" -gt 0 ]]; then
  echo
  echo "Potential overlaps detected. Review samples below."
  echo "--- shared normalized lines sample ---"
  sed -n '1,20p' "${SHARED_LINES}"
  echo "--- shared 10-grams sample ---"
  sed -n '1,20p' "${SHARED_10G}"
  exit 1
fi

echo "No direct-copy indicators detected in audited source sets."
