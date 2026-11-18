#!/usr/bin/env bash
# Smoke test of the running system, through the gateway. Used by the CI deploy job and before a demo.
#   1. /health of the gateway, the monolith and Inventory must answer 200.
#   2. The order saga: 1 × item 1 must become Confirmed, 4 × item 2 (only 3 in stock) must become Cancelled.
# Usage: scripts/smoke-test.sh            (GATEWAY=http://host:port to test another address)
# Exit code 0 = all good, 1 = something failed.
set -euo pipefail

GATEWAY=${GATEWAY:-http://localhost:5000}
JAR=$(mktemp)
trap 'rm -f "$JAR"' EXIT
failures=0

fail() { echo "FAIL: $*"; failures=$((failures + 1)); }

# --- 1. Health -------------------------------------------------------------------------------
wait_for_health() {
  local path=$1
  for _ in $(seq 1 60); do
    if [ "$(curl -s -o /dev/null -w '%{http_code}' "$GATEWAY$path")" = 200 ]; then
      echo "ok   $path answers 200"
      return 0
    fi
    sleep 3
  done
  fail "$path did not answer 200 within 3 minutes"
}

wait_for_health /health             # the gateway itself
wait_for_health /health/monolith    # the monolith's /health (its databases), through the gateway
wait_for_health /health/inventory   # Inventory's /health (InventoryDb), through the gateway

# --- 2. The order saga -------------------------------------------------------------------------
# The shop's forms need the anti-forgery token from the page and the cookie that belongs to it.
token() { grep -oE '__RequestVerificationToken" type="hidden" value="[^"]*"' | head -1 | sed 's/.*value="//; s/"$//'; }
page() { curl -s -c "$JAR" -b "$JAR" "$GATEWAY$1"; }
post() { local path=$1; shift; curl -s -o /dev/null -c "$JAR" -b "$JAR" -X POST "$GATEWAY$path" "$@"; }

login() {
  rm -f "$JAR"
  local t; t=$(page /Identity/Account/Login | token)
  post /Identity/Account/Login --data-urlencode "Input.Email=demouser@microsoft.com" \
    --data-urlencode "Input.Password=Pass@word1" --data-urlencode "Input.RememberMe=false" \
    --data-urlencode "__RequestVerificationToken=$t"
}

# My Orders as lines of "<order id> <status>", newest last.
my_orders() {
  page /order/my-orders | grep -oE 'esh-orders-item col-xs-2">[^<]*<' | sed 's/.*">//; s/<$//' \
    | paste - - - | awk -F'\t' '{print $1, $3}' | sort -n
}

# order <item id> <quantity> <expected status>
order() {
  local item=$1 quantity=$2 expected=$3 t
  for _ in $(seq 1 "$quantity"); do
    t=$(page / | token)
    post /Basket --data-urlencode "id=$item" --data-urlencode "name=x" --data-urlencode "pictureUri=x" \
      --data-urlencode "price=1" --data-urlencode "__RequestVerificationToken=$t"
  done
  t=$(page /Basket/Checkout | token)
  post /Basket/Checkout --data-urlencode "__RequestVerificationToken=$t"

  # The answer from Inventory arrives asynchronously, so poll My Orders for up to 30 seconds.
  local id status
  for _ in $(seq 1 15); do
    read -r id status < <(my_orders | tail -1)
    [ "$status" != Pending ] && break
    sleep 2
  done
  if [ "$status" = "$expected" ]; then
    echo "ok   order $id ($quantity × item $item) is $status"
  else
    fail "order $id ($quantity × item $item) is ${status:-missing}, expected $expected"
  fi
}

login
order 1 1 Confirmed
order 2 4 Cancelled

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed"
  exit 1
fi
echo "All checks passed"
