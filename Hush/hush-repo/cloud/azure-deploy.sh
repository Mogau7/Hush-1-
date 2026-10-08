set -euo pipefail
: "${SQL_PASS:?set SQL_PASS}"; : "${FIZZ_KEY:?set FIZZ_KEY}"; : "${FIZZ_ADMIN:?set FIZZ_ADMIN}"
RG=hush-rg; LOC=${LOC:-southafricanorth}; SQL=hush-sql-${SUF:-$RANDOM}

az group create -n $RG -l $LOC
az sql server create -g $RG -n $SQL -l $LOC -u hushadmin -p "$SQL_PASS"
az sql server firewall-rule create -g $RG -s $SQL -n azure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
az sql db create -g $RG -s $SQL -n Hush --service-objective S0
az containerapp env create -g $RG -n hush-env -l $LOC

az containerapp up -g $RG -n hush-mint --environment hush-env --source ./mint --ingress internal --target-port 8081 \
  --env-vars FIZZ_KEY="$FIZZ_KEY" FIZZ_ADMIN="$FIZZ_ADMIN"
CS="Server=tcp:$SQL.database.windows.net,1433;Database=Hush;User Id=hushadmin;Password=$SQL_PASS;Encrypt=true"
az containerapp up -g $RG -n hush-api --environment hush-env --source ./api --ingress external --target-port 8080 \
  --env-vars "ConnectionStrings__db=$CS" "MINT_URL=http://hush-mint"

API=https://$(az containerapp show -g $RG -n hush-api --query properties.configuration.ingress.fqdn -o tsv)
mkdir -p /tmp/hush-web && sed "s|<meta name=\"hush-api\" content=\"\">|<meta name=\"hush-api\" content=\"$API\">|" web/index.html > /tmp/hush-web/index.html
az staticwebapp create -g $RG -n hush-web -l westeurope
TOKEN=$(az staticwebapp secrets list -g $RG -n hush-web --query properties.apiKey -o tsv)
npx --yes @azure/static-web-apps-cli deploy /tmp/hush-web --deployment-token "$TOKEN" --env production
echo "API: $API"
