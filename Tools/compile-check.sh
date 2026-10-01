#!/usr/bin/env bash
# Unity'yi acmadan C# derleme kontrolu (Roslyn, ~1 dk). BrawAnimals'taki betigin uyarlamasi.
# Unity'nin son derlemede urettigi response dosyalarini (referanslar, define'lar) kullanir;
# kaynak listesini Assets'ten YENIDEN tarar, yani yeni eklenen .cs dosyalari da derlenir.
#
# Kullanim:  bash Tools/compile-check.sh
# Cikti:     hata satirlari + "Assembly-CSharp errors: N" / "Assembly-CSharp-Editor errors: N"
#
# Kendi .asmdef'i olan klasorler (Mirror, Steamworks.NET) Unity'de ayri derlenir; burada
# Library/ScriptAssemblies'teki hazir DLL'leri referans aliniyor. Unity bunlari henuz
# derlemediyse baska klasor verilebilir: EXTRA_REFS=/yol/ScriptAssemblies bash Tools/compile-check.sh
set -u
cd "$(dirname "$0")/.."
UNITY="C:/Program Files/Unity/Hub/Editor/6000.0.49f1/Editor/Data"
CSC="$UNITY/DotNetSdkRoslyn/csc.dll"
TMP="$(cygpath -m "${TMPDIR:-/tmp}")/playersmarket-compile"; mkdir -p "$TMP"
DAG=$(ls -dt Library/Bee/artifacts/*Dbg.dag 2>/dev/null | head -1)
[ -z "$DAG" ] && DAG=$(ls -dt Library/Bee/artifacts/*.dag | head -1)
REFDIR="${EXTRA_REFS:-Library/ScriptAssemblies}"

# Ayri derlenen klasorler: asmdef klasorleri + Plugins
{ find Assets -name "*.asmdef" -printf "%h/\n"; echo "Assets/Plugins/"; } > "$TMP/skip.txt"

list() { find Assets -name "*.cs" | grep -v -F -f "$TMP/skip.txt"; }

# Projedeki asmdef'lerin DLL'leri (rsp'de yoksa ekle). $1=1 ise editor DLL'leri de.
asmrefs() {
  find Assets -name "*.asmdef" -print0 | xargs -0 grep -ho '"name": *"[^"]*"' | sed 's/.*"\([^"]*\)"$/\1/' |
  while read -r n; do
    [ "$1" = 0 ] && echo "$n" | grep -qiE "editor|codegen" && continue
    [ -f "$REFDIR/$n.dll" ] && echo "-r:\"$(cygpath -m "$REFDIR/$n.dll")\""
  done
  # asmdef'siz ama asmdef'lerin kullandigi DLL'ler
  for n in kcp2k Telepathy; do [ -f "$REFDIR/$n.dll" ] && echo "-r:\"$(cygpath -m "$REFDIR/$n.dll")\""; done
}

build() {  # $1 = Unity rsp adi, $2 = kaynak listesi komutu, $3 = atilacak -r deseni, $4 = editor dll'leri
  local src="$DAG/$1.rsp" out="$TMP/$1.rsp"
  [ -f "$src" ] || { echo "rsp yok: $src (Unity projeyi bir kez derlesin)"; return; }
  grep -v '^"Assets/.*\.cs"$' "$src" | grep -v '^-out:' | grep -v -e "$3" > "$out"
  asmrefs "$4" | while read -r r; do f=${r##*/}; grep -qF "${f%\"}" "$out" || echo "$r"; done | sort -u >> "$out"
  echo "-out:\"$TMP/$1.dll\"" >> "$out"
  eval "$2" | sed 's#^#"#; s#$#"#' >> "$out"
  local result; result=$(dotnet "$CSC" "@$out" 2>&1)
  echo "$result" | grep -E "error CS" | head -30
  echo "$1 errors: $(echo "$result" | grep -cE 'error CS')"
}

build "Assembly-CSharp" 'list | grep -v "/Editor/"' '^$NEVER' 0
# Editor: oyun kodu + editor kodu birlikte (eski Assembly-CSharp.dll yerine guncel kaynak)
build "Assembly-CSharp-Editor" 'list' 'ScriptAssemblies/Assembly-CSharp.dll' 1
