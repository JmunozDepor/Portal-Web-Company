<#
.SYNOPSIS
    Prueba de funcionamiento de los procedimientos de aprobacion nativos de SAP B1
    (Approval Templates sobre Drafts) a traves del Service Layer.

.DESCRIPTION
    Valida si el portal puede apoyarse en la aprobacion nativa de SAP para Ofertas de Compra.
    Escenarios:
      Diagnostico          Solo lectura. Usuarios, plantillas activas y que plantilla aplicaria SAP
                           a una Oferta copiada del molde. No crea nada.
      AprobarSesionPropia  Crea Oferta -> el tecnico intenta aprobar (esperado: error) -> el
                           aprobador A aprueba con su propia sesion SL -> SaveDraftToDocument.
      AprobarConClave      Crea Oferta -> el tecnico aprueba enviando usuario+clave del
                           aprobador B en el cuerpo -> SaveDraftToDocument.
      Rechazo              Crea Oferta -> A rechaza -> SaveDraftToDocument (esperado: error).
      DigitadaEnSap        Manual: pide crear una Oferta en el cliente SAP con otro autor y
                           revisa si aparece en ApprovalRequests.

    Todas las Ofertas creadas llevan en Comments "PRUEBA APROBACION PORTAL <fecha-hora>" para
    ubicarlas y cerrarlas despues. Las claves se piden al ejecutar y nunca se escriben en el log.

.EXAMPLE
    # Primero, solo diagnostico (no crea documentos):
    .\PruebaAprobacionNativa.ps1 -Escenarios Diagnostico

    # Prueba completa:
    .\PruebaAprobacionNativa.ps1
#>
[CmdletBinding()]
param(
    [string]$ServiceLayerUrl = 'https://depor-sl.sapenlanube.com:50000/b1s/v1',
    [string]$CompanyDB = 'CLTSTDEPOR_OMS',
    [string]$UsuarioTecnico = 'ssas_user',
    [string]$AprobadorA = 'jmunoz',
    [string]$AprobadorB = 'manager',
    [int]$OfertaMolde = 1849,
    [ValidateSet('Diagnostico', 'AprobarSesionPropia', 'AprobarConClave', 'Rechazo', 'DigitadaEnSap')]
    [string[]]$Escenarios = @('Diagnostico', 'AprobarSesionPropia', 'AprobarConClave', 'Rechazo', 'DigitadaEnSap'),
    [switch]$OmitirValidacionCertificado,
    [string]$CarpetaLog = (Join-Path $PSScriptRoot 'logs')
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
# El Apache delante del SL renegocia TLS y responde 500 sin cuerpo a todo POST que lleve
# "Expect: 100-continue" (lo envia .NET Framework por defecto; Postman/curl no). Debe
# desactivarse antes de la primera peticion: el ServicePoint guarda el valor al crearse.
[Net.ServicePointManager]::Expect100Continue = $false

if ($OmitirValidacionCertificado -and $PSVersionTable.PSVersion.Major -lt 6) {
    if (-not ('ConfiarTodoCertificado' -as [type])) {
        Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class ConfiarTodoCertificado : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint s, X509Certificate c, WebRequest r, int p) { return true; }
}
"@
    }
    [Net.ServicePointManager]::CertificatePolicy = New-Object ConfiarTodoCertificado
}

$ServiceLayerUrl = $ServiceLayerUrl.TrimEnd('/')
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$tag = "PRUEBA APROBACION PORTAL $runId"
New-Item -ItemType Directory -Force -Path $CarpetaLog | Out-Null
$logPath = Join-Path $CarpetaLog "aprobacion-nativa-$runId.json"

$script:Pasos = New-Object System.Collections.ArrayList
$script:Credenciales = @{}
$script:Sesiones = @{}
$script:UsuariosPorId = @{}

# ---------------------------------------------------------------- utilidades

function Guardar-Log {
    $doc = [ordered]@{
        Ejecucion = [ordered]@{
            RunId = $runId; Etiqueta = $tag; ServiceLayer = $ServiceLayerUrl; CompanyDB = $CompanyDB
            UsuarioTecnico = $UsuarioTecnico; AprobadorA = $AprobadorA; AprobadorB = $AprobadorB
            OfertaMolde = $OfertaMolde; Escenarios = $Escenarios
        }
        Pasos = $script:Pasos
    }
    $doc | ConvertTo-Json -Depth 30 | Out-File -FilePath $logPath -Encoding utf8
}

function Ocultar-Secretos([string]$texto) {
    if (-not $texto) { return $texto }
    return ($texto -replace '("(?:Password|ApproverPassword)"\s*:\s*")[^"]*(")', '$1***$2')
}

function Escribir-Titulo([string]$t) {
    Write-Host ''
    Write-Host ('=' * 78) -ForegroundColor Cyan
    Write-Host "  $t" -ForegroundColor Cyan
    Write-Host ('=' * 78) -ForegroundColor Cyan
}

function Invoke-SL {
    param(
        [Parameter(Mandatory)] $Sesion,
        [string]$Metodo = 'GET',
        [Parameter(Mandatory)] [string]$Ruta,
        $Cuerpo,
        [string]$Paso = '',
        [ValidateSet('ok', 'error', '')] [string]$Esperado = ''
    )
    $json = $null
    if ($null -ne $Cuerpo) {
        if ($Cuerpo -is [string]) { $json = $Cuerpo } else { $json = $Cuerpo | ConvertTo-Json -Depth 20 }
    }
    $p = @{
        Uri             = "$ServiceLayerUrl/$Ruta"
        Method          = $Metodo
        UseBasicParsing = $true
        ContentType     = 'application/json; charset=utf-8'
        Headers         = @{ Prefer = 'odata.maxpagesize=100' }
    }
    $p.WebSession = $Sesion.Web
    if ($json) { $p.Body = [Text.Encoding]::UTF8.GetBytes($json) }
    if ($OmitirValidacionCertificado -and $PSVersionTable.PSVersion.Major -ge 6) { $p.SkipCertificateCheck = $true }

    $status = 0; $texto = $null; $setCookie = $null
    try {
        $r = Invoke-WebRequest @p
        $status = [int]$r.StatusCode
        $texto = $r.Content
        $setCookie = $r.Headers['Set-Cookie']
    }
    catch {
        $resp = $_.Exception.Response
        if ($resp) { $status = [int]$resp.StatusCode }
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $texto = $_.ErrorDetails.Message }
        elseif ($resp -and $resp -is [System.Net.HttpWebResponse]) {
            try { $texto = (New-Object IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch { $texto = $_.Exception.Message }
        }
        else { $texto = $_.Exception.Message }
    }

    $datos = $null
    if ($texto) { try { $datos = $texto | ConvertFrom-Json } catch { } }
    $ok = ($status -ge 200 -and $status -lt 300)
    $mensajeError = $null
    if (-not $ok) {
        if ($datos -and $datos.error) { $mensajeError = "[$($datos.error.code)] $($datos.error.message.value)" }
        elseif ($texto) { $mensajeError = $texto }
        else { $mensajeError = "HTTP $status sin cuerpo de respuesta" }
    }

    $resultado = ''
    if ($Esperado -eq 'ok') { if ($ok) { $resultado = 'COMO SE ESPERABA' } else { $resultado = 'INESPERADO' } }
    elseif ($Esperado -eq 'error') { if ($ok) { $resultado = 'INESPERADO' } else { $resultado = 'COMO SE ESPERABA' } }

    $respuestaLog = Ocultar-Secretos $texto
    if ($respuestaLog -and $respuestaLog.Length -gt 100000) { $respuestaLog = $respuestaLog.Substring(0, 100000) + '...(truncado)' }
    [void]$script:Pasos.Add([ordered]@{
            Hora      = (Get-Date).ToString('s')
            Paso      = $Paso
            Usuario   = $Sesion.Usuario
            Metodo    = $Metodo
            Ruta      = $Ruta
            Status    = $status
            Ok        = $ok
            Esperado  = $Esperado
            Resultado = $resultado
            Error     = $mensajeError
            Request   = (Ocultar-Secretos $json)
            Response  = $respuestaLog
        })
    Guardar-Log

    $color = 'Green'; if (-not $ok) { $color = 'Red' }
    if ($resultado -eq 'COMO SE ESPERABA' -and -not $ok) { $color = 'Yellow' }
    $sufijo = ''; if ($resultado) { $sufijo = "  <- $resultado" }
    Write-Host ("  [{0}] {1,-6} {2}  ({3}){4}" -f $status, $Metodo, $Ruta, $Sesion.Usuario, $sufijo) -ForegroundColor $color
    if ($mensajeError) { Write-Host "         $mensajeError" -ForegroundColor DarkGray }

    return [pscustomobject]@{ Status = $status; Ok = $ok; Datos = $datos; Error = $mensajeError; SetCookie = $setCookie }
}

function Obtener-Credencial([string]$usuario) {
    if (-not $script:Credenciales.ContainsKey($usuario)) {
        $script:Credenciales[$usuario] = Get-Credential -UserName $usuario -Message "Clave SAP de '$usuario' en $CompanyDB"
    }
    return $script:Credenciales[$usuario]
}

function Obtener-Sesion([string]$usuario) {
    if ($script:Sesiones.ContainsKey($usuario)) { return $script:Sesiones[$usuario] }
    $cred = Obtener-Credencial $usuario
    $s = [pscustomobject]@{ Usuario = $usuario; Web = (New-Object Microsoft.PowerShell.Commands.WebRequestSession) }
    $body = @{ CompanyDB = $CompanyDB; UserName = $usuario; Password = $cred.GetNetworkCredential().Password }
    $r = Invoke-SL -Sesion $s -Metodo POST -Ruta 'Login' -Cuerpo $body -Paso "Login $usuario"
    if (-not $r.Ok) { throw "No se pudo iniciar sesion SL con '$usuario': $($r.Error)" }

    # .NET Framework no reenvia bien las cookies que devuelve este SL (y descarta cualquier
    # cabecera Cookie manual), asi que se cargan explicitas en el contenedor con path "/":
    # B1SESSION = SessionId del cuerpo; ROUTEID = nodo del balanceador que tiene la sesion.
    $hostSl = ([Uri]$ServiceLayerUrl).Host
    $todas = ($r.SetCookie | ForEach-Object { "$_" }) -join ', '
    $nombres = ([regex]::Matches($todas, '(?:^|,\s*)([A-Za-z0-9_]+)=') | ForEach-Object { $_.Groups[1].Value }) -join ', '
    $ruta = [regex]::Match($todas, 'ROUTEID=([^;,\s]+)')
    $s.Web.Cookies = New-Object System.Net.CookieContainer
    $s.Web.Cookies.Add((New-Object System.Net.Cookie('B1SESSION', "$($r.Datos.SessionId)", '/', $hostSl)))
    $rutaTexto = '(sin ROUTEID)'
    if ($ruta.Success) {
        $s.Web.Cookies.Add((New-Object System.Net.Cookie('ROUTEID', $ruta.Groups[1].Value, '/', $hostSl)))
        $rutaTexto = "ROUTEID=$($ruta.Groups[1].Value)"
    }
    Write-Host "         Set-Cookie recibidas: [$nombres]  ->  sesion fijada con B1SESSION + $rutaTexto" -ForegroundColor DarkGray
    $script:Sesiones[$usuario] = $s
    return $s
}

function Cerrar-Sesiones {
    foreach ($s in $script:Sesiones.Values) {
        try { [void](Invoke-SL -Sesion $s -Metodo POST -Ruta 'Logout' -Paso "Logout $($s.Usuario)") } catch { }
    }
}

function Nombre-Usuario($id) {
    if ($null -eq $id) { return '' }
    $k = [int]$id
    if ($script:UsuariosPorId.ContainsKey($k)) { return "$($script:UsuariosPorId[$k]) (#$k)" }
    return "#$k"
}

function Pausa([string]$mensaje) {
    Write-Host ''
    Write-Host "  >> $mensaje" -ForegroundColor Magenta
    [void](Read-Host '     Presiona Enter para continuar')
}

# ---------------------------------------------------------------- documento

$script:Molde = $null

function Copiar-Si($origen, $destino, [string[]]$campos) {
    foreach ($c in $campos) {
        $prop = $origen.PSObject.Properties[$c]
        if ($prop -and $null -ne $prop.Value -and "$($prop.Value)" -ne '') { $destino[$c] = $prop.Value }
    }
}

function Copiar-UdfDep($origen, $destino) {
    foreach ($prop in $origen.PSObject.Properties) {
        if ($prop.Name -like 'U_DEP_*' -and $null -ne $prop.Value -and "$($prop.Value)" -ne '') { $destino[$prop.Name] = $prop.Value }
    }
}

function Nuevo-Payload([string]$etiqueta) {
    $m = $script:Molde
    $hoy = (Get-Date).ToString('yyyy-MM-dd')
    $venc = (Get-Date).AddDays(7).ToString('yyyy-MM-dd')
    $doc = [ordered]@{
        CardCode     = $m.CardCode
        DocType      = $m.DocType
        DocDate      = $hoy
        TaxDate      = $hoy
        DocDueDate   = $venc
        RequriedDate = $venc
        Comments     = "$tag - $etiqueta"
    }
    Copiar-Si $m $doc @('DocCurrency', 'SalesPersonCode', 'DocumentsOwner', 'Project', 'BPL_IDAssignedToInvoice', 'NumAtCard')
    Copiar-UdfDep $m $doc

    $esServicio = ($m.DocType -eq 'dDocument_Service')
    $lineas = @()
    foreach ($l in $m.DocumentLines) {
        $ln = [ordered]@{ RequiredDate = $venc }
        Copiar-Si $l $ln @('ItemCode', 'ItemDescription', 'Quantity', 'UnitPrice', 'Currency', 'WarehouseCode', 'TaxCode',
            'CostingCode', 'CostingCode2', 'CostingCode3', 'CostingCode4', 'CostingCode5', 'ProjectCode')
        if ($esServicio) { Copiar-Si $l $ln @('AccountCode', 'LineTotal') }
        Copiar-UdfDep $l $ln
        $lineas += $ln
    }
    $doc['DocumentLines'] = $lineas
    return $doc
}

function Comparar-Documento($payload, $creado) {
    $diferencias = @()
    foreach ($k in $payload.Keys) {
        if ($k -eq 'DocumentLines' -or $k -in @('DocDate', 'TaxDate', 'Comments')) { continue }
        $a = "$($payload[$k])"; $b = "$($creado.$k)"
        if ($k -like '*Date') { $b = $b.Substring(0, [Math]::Min(10, $b.Length)) }
        if ($a -ne $b) { $diferencias += "Cabecera.$k  enviado='$a'  quedo='$b'" }
    }
    $lineasP = @($payload['DocumentLines'])
    $lineasC = @($creado.DocumentLines)
    if ($lineasP.Count -ne $lineasC.Count) { $diferencias += "Lineas: enviadas=$($lineasP.Count) quedaron=$($lineasC.Count)" }
    for ($i = 0; $i -lt [Math]::Min($lineasP.Count, $lineasC.Count); $i++) {
        foreach ($k in $lineasP[$i].Keys) {
            if ($k -in @('ItemDescription', 'UnitPrice', 'LineTotal')) { continue }
            $a = "$($lineasP[$i][$k])"; $b = "$($lineasC[$i].$k)"
            if ($k -like '*Date') { $b = $b.Substring(0, [Math]::Min(10, $b.Length)) }
            if ($k -eq 'Quantity') { $a = [double]$a; $b = [double]$b; $a = "$a"; $b = "$b" }
            if ($a -ne $b) { $diferencias += "Linea[$i].$k  enviado='$a'  quedo='$b'" }
        }
    }
    if ($diferencias.Count -eq 0) {
        Write-Host '     Campos U_DEP_*, centros de costo, almacen y fechas: SIN DIFERENCIAS' -ForegroundColor Green
    }
    else {
        Write-Host '     Diferencias entre lo enviado y la Oferta real:' -ForegroundColor Yellow
        $diferencias | ForEach-Object { Write-Host "       - $_" -ForegroundColor Yellow }
    }
    [void]$script:Pasos.Add([ordered]@{ Hora = (Get-Date).ToString('s'); Paso = 'Comparacion borrador vs Oferta real'; Diferencias = $diferencias })
    Guardar-Log
}

function Max-Codigo($sesion, [string]$entidad, [string]$campo) {
    $r = Invoke-SL -Sesion $sesion -Ruta ($entidad + '?$select=' + $campo + '&$orderby=' + $campo + ' desc&$top=1') -Paso "Ultimo $campo de $entidad"
    if ($r.Ok -and $r.Datos.value -and @($r.Datos.value).Count -gt 0) { return [int]@($r.Datos.value)[0].$campo }
    return 0
}

function Mostrar-Solicitud($ar) {
    Write-Host ("     Solicitud Code={0}  Status={1}  ObjectType={2}  IsDraft={3}  ObjectEntry={4}  DraftEntry={5}  Etapa={6}  Plantilla={7}  Autor={8}" -f `
            $ar.Code, $ar.Status, $ar.ObjectType, $ar.IsDraft, $ar.ObjectEntry, $ar.DraftEntry, $ar.CurrentStage, $ar.ApprovalTemplatesID, (Nombre-Usuario $ar.OriginatorID)) -ForegroundColor White
    foreach ($l in @($ar.ApprovalRequestLines)) {
        if ($null -eq $l) { continue }
        Write-Host ("       etapa {0}  aprobador {1,-22} {2,-15} {3} {4}" -f $l.StageCode, (Nombre-Usuario $l.UserID), $l.Status, $l.UpdateDate, $l.Remarks) -ForegroundColor Gray
    }
}

function Leer-Solicitud($sesion, [int]$code, [string]$paso) {
    $r = Invoke-SL -Sesion $sesion -Ruta "ApprovalRequests($code)" -Paso $paso
    if ($r.Ok) { Mostrar-Solicitud $r.Datos }
    return $r.Datos
}

# Crea la Oferta como usuario tecnico y devuelve la ApprovalRequest generada (o $null).
function Crear-OfertaConAprobacion([string]$etiqueta) {
    $tec = Obtener-Sesion $UsuarioTecnico
    $maxAr = Max-Codigo $tec 'ApprovalRequests' 'Code'
    $maxPq = Max-Codigo $tec 'PurchaseQuotations' 'DocEntry'
    $payload = Nuevo-Payload $etiqueta

    $r = Invoke-SL -Sesion $tec -Metodo POST -Ruta 'PurchaseQuotations' -Cuerpo $payload -Paso "[$etiqueta] Crear Oferta de Compra"
    if ($r.Ok -and $r.Datos -and $r.Datos.DocEntry) {
        Write-Host "     SAP creo la Oferta REAL directamente (DocEntry $($r.Datos.DocEntry), DocNum $($r.Datos.DocNum))." -ForegroundColor Yellow
        Write-Host '     La plantilla de aprobacion NO se aplico. Revisa la Fase 1 (autor = usuario tecnico, documento, terminos, activa).' -ForegroundColor Yellow
        return $null
    }

    $q = Invoke-SL -Sesion $tec -Ruta ('ApprovalRequests?$filter=Code gt ' + $maxAr + '&$orderby=Code desc') -Paso "[$etiqueta] Buscar solicitud de aprobacion nueva"
    $nuevas = @(); if ($q.Ok -and $q.Datos.value) { $nuevas = @($q.Datos.value) }
    if ($nuevas.Count -eq 0) {
        Write-Host '     No aparecio ninguna ApprovalRequest nueva.' -ForegroundColor Red
        return $null
    }
    $ar = $nuevas[0]
    Mostrar-Solicitud $ar

    $draft = $ar.DraftEntry; if (-not $draft -or [int]$draft -le 0) { $draft = $ar.ObjectEntry }
    $d = Invoke-SL -Sesion $tec -Ruta ("Drafts($draft)" + '?$select=DocEntry,DocNum,DocObjectCode,DocumentStatus,Comments') -Paso "[$etiqueta] Leer borrador"
    if ($d.Ok) { Write-Host "     Borrador DocEntry=$($d.Datos.DocEntry) DocObjectCode=$($d.Datos.DocObjectCode) Estado=$($d.Datos.DocumentStatus)" -ForegroundColor White }

    return [pscustomobject]@{ Code = [int]$ar.Code; Draft = [int]$draft; Payload = $payload; MaxPq = $maxPq; Etiqueta = $etiqueta }
}

function Decidir($sesion, [int]$code, [string]$estado, [string]$paso, [string]$esperado, [string]$usuarioAprobador) {
    $decision = [ordered]@{ Status = $estado; Remarks = "$tag - $paso" }
    if ($usuarioAprobador) {
        $cred = Obtener-Credencial $usuarioAprobador
        $decision['ApproverUserName'] = $usuarioAprobador
        $decision['ApproverPassword'] = $cred.GetNetworkCredential().Password
    }
    $body = [ordered]@{ ApprovalRequestDecisions = @($decision) }
    return Invoke-SL -Sesion $sesion -Metodo PATCH -Ruta "ApprovalRequests($code)" -Cuerpo $body -Paso $paso -Esperado $esperado
}

function Generar-Documento($caso, [string]$esperado) {
    $tec = Obtener-Sesion $UsuarioTecnico
    $body = @{ Document = @{ DocEntry = $caso.Draft } }
    $r = Invoke-SL -Sesion $tec -Metodo POST -Ruta 'DraftsService_SaveDraftToDocument' -Cuerpo $body -Paso "[$($caso.Etiqueta)] SaveDraftToDocument" -Esperado $esperado
    if (-not $r.Ok) { return }

    $q = Invoke-SL -Sesion $tec -Ruta ('PurchaseQuotations?$filter=DocEntry gt ' + $caso.MaxPq + '&$orderby=DocEntry desc&$select=DocEntry,DocNum,Comments') -Paso "[$($caso.Etiqueta)] Buscar Oferta real generada"
    $doc = $null
    if ($q.Ok) { $doc = @($q.Datos.value) | Where-Object { $_.Comments -like "*$($caso.Etiqueta)*" } | Select-Object -First 1 }
    if (-not $doc) { Write-Host '     No se encontro la Oferta real generada.' -ForegroundColor Red; return }
    Write-Host "     Oferta real creada: DocEntry=$($doc.DocEntry) DocNum=$($doc.DocNum)" -ForegroundColor Green
    $full = Invoke-SL -Sesion $tec -Ruta "PurchaseQuotations($($doc.DocEntry))" -Paso "[$($caso.Etiqueta)] Leer Oferta real"
    if ($full.Ok) { Comparar-Documento $caso.Payload $full.Datos }
}

# ---------------------------------------------------------------- escenarios

function Preparar {
    Escribir-Titulo 'Preparacion: sesion tecnica, usuarios y molde'
    $tec = Obtener-Sesion $UsuarioTecnico

    $filtro = "UserCode eq '$UsuarioTecnico' or UserCode eq '$AprobadorA' or UserCode eq '$AprobadorB'"
    $u = Invoke-SL -Sesion $tec -Ruta ('Users?$select=InternalKey,UserCode,UserName&$filter=' + $filtro) -Paso 'Ids de usuarios'
    if ($u.Ok) {
        foreach ($x in @($u.Datos.value)) { $script:UsuariosPorId[[int]$x.InternalKey] = $x.UserCode; Write-Host "     $($x.UserCode) = #$($x.InternalKey) ($($x.UserName))" -ForegroundColor Gray }
    }

    $m = Invoke-SL -Sesion $tec -Ruta "PurchaseQuotations($OfertaMolde)" -Paso "Leer Oferta molde $OfertaMolde"
    if (-not $m.Ok) { throw "No se pudo leer la Oferta molde $OfertaMolde" }
    $script:Molde = $m.Datos
    Write-Host "     Molde: DocNum=$($m.Datos.DocNum) Proveedor=$($m.Datos.CardCode) Tipo=$($m.Datos.DocType) Lineas=$(@($m.Datos.DocumentLines).Count)" -ForegroundColor Gray
}

function Escenario-Diagnostico {
    Escribir-Titulo 'Diagnostico (solo lectura)'
    $tec = Obtener-Sesion $UsuarioTecnico

    $t = Invoke-SL -Sesion $tec -Ruta 'ApprovalTemplates' -Paso 'Plantillas de aprobacion'
    if ($t.Ok) {
        foreach ($x in @($t.Datos.value)) {
            $autores = (@($x.ApprovalTemplateUsers) | ForEach-Object { Nombre-Usuario $_.UserID }) -join ', '
            $docs = (@($x.ApprovalTemplateDocuments) | ForEach-Object { $_.DocumentType }) -join ', '
            $etapas = (@($x.ApprovalTemplateStages) | ForEach-Object { $_.ApprovalStageCode }) -join ', '
            Write-Host ("     [{0}] {1}  activa={2}  autores=[{3}]  documentos=[{4}]  etapas=[{5}]" -f $x.Code, $x.Name, $x.IsActive, $autores, $docs, $etapas) -ForegroundColor Gray
        }
    }

    [void](Invoke-SL -Sesion $tec -Ruta 'ApprovalStages' -Paso 'Etapas de aprobacion')

    $payload = Nuevo-Payload 'Diagnostico'
    $g = Invoke-SL -Sesion $tec -Metodo POST -Ruta 'PurchaseQuotationsService_GetApprovalTemplates' -Cuerpo @{ Document = $payload } -Paso 'Que plantilla aplicaria SAP a la Oferta' -Esperado 'ok'
    if ($g.Ok) {
        $aplica = @($g.Datos.Document_ApprovalRequests) | Where-Object { $_ }
        if ($aplica.Count -gt 0) {
            $aplica | ForEach-Object { Write-Host "     SAP aplicaria la plantilla #$($_.ApprovalTemplatesID) '$($_.ApprovalTemplatesName)'" -ForegroundColor Green }
        }
        else {
            Write-Host '     SAP NO aplicaria ninguna plantilla a esta Oferta creada por el usuario tecnico.' -ForegroundColor Yellow
            Write-Host '     Si sigues, la Oferta se creara como documento real sin aprobacion.' -ForegroundColor Yellow
        }
    }
}

function Escenario-AprobarSesionPropia {
    Escribir-Titulo "Escenario: el aprobador ($AprobadorA) aprueba con su propia sesion SL"
    $caso = Crear-OfertaConAprobacion 'SesionPropia'
    if (-not $caso) { return }
    $tec = Obtener-Sesion $UsuarioTecnico

    [void](Decidir $tec $caso.Code 'ardApproved' '[SesionPropia] Tecnico aprueba sin credenciales' 'error' $null)

    $a = Obtener-Sesion $AprobadorA
    [void](Invoke-SL -Sesion $a -Metodo POST -Ruta 'ApprovalRequestsService_GetOpenApprovalRequestList' -Paso "[SesionPropia] Bandeja abierta de $AprobadorA")
    [void](Decidir $a $caso.Code 'ardApproved' "[SesionPropia] $AprobadorA aprueba con su sesion" 'ok' $null)
    [void](Leer-Solicitud $tec $caso.Code '[SesionPropia] Estado tras aprobar')

    Generar-Documento $caso 'ok'
}

function Escenario-AprobarConClave {
    Escribir-Titulo "Escenario: el tecnico aprueba en nombre de $AprobadorB enviando su usuario y clave"
    $caso = Crear-OfertaConAprobacion 'ConClave'
    if (-not $caso) { return }
    $tec = Obtener-Sesion $UsuarioTecnico

    [void](Decidir $tec $caso.Code 'ardApproved' "[ConClave] Tecnico aprueba con usuario+clave de $AprobadorB" 'ok' $AprobadorB)
    [void](Leer-Solicitud $tec $caso.Code '[ConClave] Estado tras aprobar')

    Generar-Documento $caso 'ok'
}

function Escenario-Rechazo {
    Escribir-Titulo "Escenario: $AprobadorA rechaza y se intenta generar el documento"
    $caso = Crear-OfertaConAprobacion 'Rechazo'
    if (-not $caso) { return }
    $tec = Obtener-Sesion $UsuarioTecnico

    $a = Obtener-Sesion $AprobadorA
    [void](Decidir $a $caso.Code 'ardNotApproved' "[Rechazo] $AprobadorA rechaza" 'ok' $null)
    [void](Leer-Solicitud $tec $caso.Code '[Rechazo] Estado tras rechazar')

    Generar-Documento $caso 'error'
    $d = Invoke-SL -Sesion $tec -Ruta ("Drafts($($caso.Draft))" + '?$select=DocEntry,DocumentStatus,DocObjectCode') -Paso '[Rechazo] Estado del borrador rechazado'
    if ($d.Ok) { Write-Host "     El borrador sigue: DocEntry=$($d.Datos.DocEntry) Estado=$($d.Datos.DocumentStatus)" -ForegroundColor White }
}

function Escenario-DigitadaEnSap {
    Escribir-Titulo 'Escenario: Oferta digitada en el cliente SAP por otro usuario'
    $tec = Obtener-Sesion $UsuarioTecnico
    $maxAr = Max-Codigo $tec 'ApprovalRequests' 'Code'
    Pausa "Crea ahora en el cliente SAP ($CompanyDB) una Oferta de Compra con un usuario distinto de '$UsuarioTecnico' que este como autor en una plantilla. Escribe '$tag' en Comentarios."

    $q = Invoke-SL -Sesion $tec -Ruta ('ApprovalRequests?$filter=Code gt ' + $maxAr + '&$orderby=Code desc') -Paso '[DigitadaEnSap] Solicitudes nuevas'
    $nuevas = @(); if ($q.Ok -and $q.Datos.value) { $nuevas = @($q.Datos.value) }
    if ($nuevas.Count -eq 0) { Write-Host '     No aparecio ninguna solicitud nueva.' -ForegroundColor Yellow }
    $nuevas | ForEach-Object { Mostrar-Solicitud $_ }

    $a = Obtener-Sesion $AprobadorA
    [void](Invoke-SL -Sesion $a -Metodo POST -Ruta 'ApprovalRequestsService_GetOpenApprovalRequestList' -Paso "[DigitadaEnSap] Bandeja abierta de $AprobadorA")
}

# ---------------------------------------------------------------- main

Write-Host "Service Layer : $ServiceLayerUrl"
Write-Host "Base          : $CompanyDB"
Write-Host "Etiqueta      : $tag"
Write-Host "Log           : $logPath"

try {
    Preparar
    foreach ($e in $Escenarios) {
        switch ($e) {
            'Diagnostico' { Escenario-Diagnostico }
            'AprobarSesionPropia' { Escenario-AprobarSesionPropia }
            'AprobarConClave' { Escenario-AprobarConClave }
            'Rechazo' { Escenario-Rechazo }
            'DigitadaEnSap' { Escenario-DigitadaEnSap }
        }
    }
}
catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    [void]$script:Pasos.Add([ordered]@{ Hora = (Get-Date).ToString('s'); Paso = 'Excepcion'; Error = $_.Exception.Message })
}
finally {
    Cerrar-Sesiones
    Guardar-Log
}

Escribir-Titulo 'Resumen'
$script:Pasos | Where-Object { $_.Esperado } | ForEach-Object {
    $color = 'Green'; if ($_.Resultado -eq 'INESPERADO') { $color = 'Red' }
    Write-Host ("  {0,-17} {1,-60} status {2}" -f $_.Resultado, $_.Paso, $_.Status) -ForegroundColor $color
}
Write-Host ''
Write-Host "Log completo (sin claves): $logPath" -ForegroundColor Cyan
Write-Host "Documentos de prueba marcados con: '$tag'" -ForegroundColor Cyan
