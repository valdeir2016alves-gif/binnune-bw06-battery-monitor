# Monitor de Bateria — Binnune BW06-BT

Aplicativo nativo e ultraleve para Windows (System Tray) criado sob medida para monitorar a bateria do headset **Binnune BW06-BT** quando conectado via receptor **USB 2.4GHz** (`VID_10D6 & PID_4801`).

## Funcionalidades

- **Ícone dinâmico ao lado do relógio:** exibe o número exato da porcentagem (`0..100`) e barra colorida na bandeja do Windows.
- **Monitoramento inteligente de áudio (WASAPI CoreAudio):** detecta automaticamente `Alto-falantes (BW06-BT)` e `Microfone (BW06-BT)` e só desconta carga quando há uso real (ajustando pela curva de 48h a 70% de volume + standby automático após 15 min em silêncio).
- **Sincronização real e uso via Bluetooth:** quando o fone está conectado via Bluetooth (`BW06-BT`, MAC `50:C0:F0:24:EB:2D`), o aplicativo lê a porcentagem real do hardware via sensor HFP do Windows (que atualiza em degraus de `10%`: `100%`, `90%`, `80%`...), interpola suavemente de `1%` em `1%` durante o áudio ativo (curva oficial de `63h` no Bluetooth) e recalibra automaticamente a cada novo degrau do sensor. Se estiver usando no receptor USB 2.4GHz, basta dar **2 cliques rápidos no botão Power** para passar 3 segundos no Bluetooth e sincronizar a bateria real!
- **Painel rápido 100% automático (clique esquerdo no ícone):** anel de bateria, horas estimadas restantes, medidor de áudio ao vivo, leitura direta do sensor Bluetooth e opção **Iniciar automaticamente com o Windows** (sem botões de ajuste manual).
- **Alertas de bateria fraca:** notificações automáticas em `20%` e `10%`.

## Detalhes técnicos do hardware (referência do projeto)

- **Chipset do dongle USB 2.4GHz:** Actions Semiconductor (`USB\VID_10D6&PID_4801`), interfaces HID `COL01..COL04` — o firmware não expõe descritor/relatório HID de bateria (`0x06`/`0x85`) no link 2.4GHz, apenas transporte de áudio USB e teclas de mídia.
- **Especificações oficiais da bateria:** 500 mAh (3.7V) — autonomia nominal de **~48h no modo 2.4GHz USB** e **~63h no modo Bluetooth** (a ~70% de volume).

## Como compilar / executar

- **Executar:** abra `BW06Monitor.exe`
- **Recompilar:** execute `.\build.ps1` no PowerShell
- **Estado salvo em:** `%LocalAppData%\BW06BatteryMonitor\battery_state.ini`
