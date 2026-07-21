#!/usr/bin/env bash
# Проверка перед пушем: ловит то, что НЕ ловит обычная сборка.
#
# Зачем: `dotnet build` проходит зелёным, а клиент может не запуститься вовсе — Content.Shared и
# Content.Client проверяются IL-вайтлистом песочницы при СТАРТЕ клиента. Один раз это стоило
# двух часов поиска: клиент «открывался и сразу закрывался», хотя сборка была чистой.
#
# Запуск из корня репозитория:
#     ./dev/precheck.sh            # всё
#     ./dev/precheck.sh fast       # без интеграционных тестов форка (быстрее)
set -uo pipefail
cd "$(dirname "$0")/.."

FAST="${1:-}"
FAILED=()

step() {
    local name="$1"; shift
    printf '\n\033[1m▶ %s\033[0m\n' "$name"
    if "$@"; then
        printf '\033[32m  ✓ %s\033[0m\n' "$name"
    else
        printf '\033[31m  ✗ %s\033[0m\n' "$name"
        FAILED+=("$name")
    fi
}

# ВАЖНО: судим по КОДУ ВОЗВРАТА, а не по грепу вывода.
# Раньше здесь был `grep -qE "error (CS|MSB)"` — и он пропускал ошибки анализаторов
# (RA0049 «[Dependency]-поля требуют partial», RA0033 и прочие). Из-за этого Release-сборка
# падала, а precheck рапортовал «всё чисто».
build() {
    local out
    if ! out=$(dotnet build "$1" -c Release 2>&1); then
        echo "$out" | grep -E "error" | head -20
        return 1
    fi
    return 0
}

lint_yaml() {
    local out rc
    out=$(dotnet run --project Content.YAMLLinter 2>&1); rc=$?
    echo "    $(echo "$out" | tail -1)"
    [[ $rc -eq 0 ]] && echo "$out" | grep -q "No errors found"
}

# Тоже по коду возврата: иначе ошибка компиляции тест-проекта, падение тест-хоста
# и «ни один тест не подошёл под фильтр» тихо считались успехом.
run_tests() {
    local out rc
    out=$(dotnet test "$1" --filter "$2" 2>&1); rc=$?
    if [[ $rc -ne 0 ]]; then
        echo "$out" | grep -iE "error|Не пройден|Failed" | head -20
        return 1
    fi
    # Строки проверены эмпирически: dotnet test при пустом фильтре возвращает 0 и пишет это.
    if echo "$out" | grep -qiE "Нет тестов, соответствующих|No test matches|No test is available"; then
        echo "    ВНИМАНИЕ: фильтр «$2» не нашёл ни одного теста — это не успех"
        return 1
    fi
    return 0
}

step "Сборка сервера"            build Content.Server/Content.Server.csproj
step "Сборка клиента"            build Content.Client/Content.Client.csproj
step "Прототипы (YAMLLinter)"    lint_yaml

# Главное: настоящая проверка песочницы теми же средствами, что и у клиента на старте.
# Ловит запрещённые вызовы (ReadOnlySpan, ImageSharp Image.Load и прочий не-вайтлистнутый API).
step "Песочница клиента"         run_tests Content.IntegrationTests "FullyQualifiedName~SandboxTest"

step "Юнит-тесты форка"          run_tests Content.Tests "FullyQualifiedName~_Wega"

if [[ "$FAST" != "fast" ]]; then
    step "Интеграционные тесты форка" run_tests Content.IntegrationTests "FullyQualifiedName~_Wega"
fi

printf '\n'
if [[ ${#FAILED[@]} -eq 0 ]]; then
    printf '\033[32m\033[1mВсё чисто — можно пушить.\033[0m\n'
    exit 0
fi

printf '\033[31m\033[1mПровалено: %s\033[0m\n' "${FAILED[*]}"
exit 1
