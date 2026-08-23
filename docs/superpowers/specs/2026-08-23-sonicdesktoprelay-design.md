# SonicDesktopRelay — design

Compartilhamento de tela entre computadores Windows sobre a API SonicRelay existente.
Cobre parcialmente [issue #30](https://github.com/vitorhugo-dotnet/dotnet_SonicRelay/issues/30):
a parte de vídeo e áudio, **sem** controle remoto.

Decisões registradas em `../../../../DECISOES-SonicDesktopRelay.md` (raiz de
`H:\Script\SonicRelay`). Contrato do backend especificado em
`dotnet_SonicRelay/docs/superpowers/specs/2026-08-23-screen-share-sessions-design.md`.

## Objetivo

Um aplicativo Windows que permite a uma máquina transmitir a imagem de um monitor e o áudio
do sistema, e a outras máquinas assistirem. O mesmo executável faz os dois papéis. Quem
assiste precisa apenas do código de sessão que quem compartilha exibe na tela.

A API continua sendo control-plane: autentica, autoriza, mantém sessões e encaminha
signaling. Ela não recebe, armazena, transcodifica nem retransmite mídia — a
[ADR 0001](https://github.com/vitorhugo-dotnet/dotnet_SonicRelay/blob/main/docs/adr/0001-control-plane-only.md)
continua valendo integralmente. Mídia trafega P2P, ou pelo coturn quando o caminho direto
falha.

## Escopo

Dentro:

- Publicar a imagem de **um** monitor à escolha, com o áudio do sistema da mesma máquina.
- Assistir à tela de outra máquina, com o áudio.
- Vários espectadores por sessão.
- Registro automático do device na primeira execução, sem login.
- Entrada por código de sessão único, que estabelece o pareamento.
- Cinco superfícies: Início, Compartilhar, Assistir, Configurações, Diagnóstico.

Fora:

- Controle remoto de mouse e teclado, e tudo o que ele arrasta (UAC, tela de bloqueio,
  elevação).
- Compartilhar janela específica, ou mais de um monitor ao mesmo tempo.
- Compartilhar e assistir simultaneamente na mesma máquina.
- Microfone, chat, transferência de arquivo, clipboard, gravação.
- Qualquer plataforma que não seja Windows.
- Qualquer mudança de comportamento em `windows_SonicRelay` ou `flutter_SonicRelay`.

## A restrição que governa o design

O usuário exigiu que nenhuma integração existente quebre. Isso tem consequência concreta em
três pontos, e cada um deles virou uma escolha de design em vez de uma boa intenção:

1. **Escopos.** `windows_publisher` não tem `session:join` nem `pairing:complete`
   (`DeviceCredentialService.cs:59-71`). Em vez de ampliar esse tipo, a Fase 0 acrescenta um
   tipo novo. Os dois tipos existentes ficam idênticos, incluindo a lista de escopos que os
   testes fixam.
2. **Viewer Flutter recebendo vídeo.** Um app que não sabe renderizar vídeo, recebendo uma
   offer com m-line de vídeo, é uma regressão. A Fase 0 impede o join por tipo de device, no
   servidor, em vez de confiar que o cliente antigo saiba se retirar.
3. **Código compartilhado.** `ApiClient`, `Signaling` e `WebRtc` são `ProjectReference`
   locais de `windows_SonicRelay`. Transformá-los em pacote exigiria mexer naquele
   repositório. O app novo reescreve o que precisa, seguindo o contrato documentado.

## Fases

O trabalho é grande demais para um plano só. Cada fase tem seu próprio plano e termina
verificável de ponta a ponta.

| Fase | Repositório | Entrega |
|---|---|---|
| 0 | `dotnet_SonicRelay` | Tipo de device, modo de sessão, auto-pareamento, restrição de join, métricas |
| 1 | `dotnet_SonicDesktopRelay` | Shell Avalonia, identidade, API, signaling, sessão — sem mídia |
| 2 | `dotnet_SonicDesktopRelay` | Captura, encode H.264, publicação de vídeo para N viewers |
| 3 | `dotnet_SonicDesktopRelay` | Decode, render, playback — o lado que assiste |
| 4 | `dotnet_SonicDesktopRelay` | Áudio do sistema, Diagnóstico, Configurações, instalador |

A Fase 0 precisa estar publicada antes de a Fase 1 conseguir sequer obter um token. As
Fases 2 e 3 são independentes entre si depois que a Fase 1 fecha: uma produz mídia, a outra
consome, e ambas se apoiam no mesmo `SessionRuntime`.

---

## Fase 0 — Contrato do backend

Detalhada em spec própria no repositório da API. Resumo do contrato que o app consome:

**Novo tipo de device.** `windows_desktop` na plataforma `windows`, com a união dos escopos:
`device:read`, `device:manage`, `pairing:create`, `pairing:complete`, `pairing:revoke`,
`session:create`, `session:join`, `session:end`, `signaling:connect`, `turn:credentials`.

**Novo modo de sessão.** `screen_share`. Para permissões de áudio comporta-se como
`broadcast`: a origem transmite, os demais recebem.

**Join.** Em sessão `screen_share`, um device `windows_desktop` que apresente um código
válido é admitido mesmo sem pareamento prévio — o join cria o `DevicePairing`. Um device de
outro tipo recebe `403 device_type_not_allowed`. Os modos `broadcast` e `duplex` continuam
exigindo pareamento anterior, sem alteração alguma.

**Sem migration.** `Mode` aceita 16 caracteres e `DeviceType` aceita 40; os valores novos
cabem.

---

## Fase 1 — Esqueleto do app

### Projetos

Layout final, alcançado ao longo das quatro fases. A Fase 1 cria `Core`, `ApiClient`,
`Signaling`, `Presentation` e `App`; `Media`, `Media.Windows` e `Rtc` nascem na Fase 2.

```text
src/SonicDesktopRelay.Core           domínio, configuração, armazenamento protegido, diagnóstico
src/SonicDesktopRelay.ApiClient      HTTP tipado: devices, sessions, pairings, ice-servers
src/SonicDesktopRelay.Signaling      WebSocket de signaling, envelope, reconexão
src/SonicDesktopRelay.Media          contratos de mídia: captura, encoder, decoder, sink
src/SonicDesktopRelay.Media.Windows  adaptadores Windows: WGC, FFmpeg, WASAPI
src/SonicDesktopRelay.Rtc            peer connections, negociação, fan-out para N viewers
src/SonicDesktopRelay.Presentation   view models, máquina de estados, projeções para a UI
src/SonicDesktopRelay.App            shell Avalonia, páginas, composição
```

A direção de dependência é sempre para dentro: `App` → `Presentation` → {`Rtc`, `Media`,
`Signaling`, `ApiClient`} → `Core`. `Media.Windows` é referenciado apenas pela composição em
`App`; nenhum view model conhece WGC, FFmpeg ou WASAPI. Isso é o que permite testar a
apresentação inteira sem uma placa de vídeo.

`Media.Windows` usa TFM `net10.0-windows10.0.19041.0`; todo o resto usa `net10.0`.

### Identidade

Na primeira execução: `POST /api/devices/bootstrap` com
`{ deviceType: "windows_desktop", platform: "windows", name: <nome da máquina> }`. O
`deviceId` e o `credentialSecret` vão para um arquivo no perfil do usuário, protegido por
DPAPI no escopo do usuário. Não há tela de login em momento algum.

Renovação: `POST /api/devices/token`, disparada quando faltar menos de 20% da validade do
token atual. Se a resposta trouxer `rotatedCredentialSecret` não nulo, o app substitui
`deviceId` **e** segredo pelos novos antes de qualquer outra chamada — a identidade antiga
deixou de existir e o próximo uso dela retornaria `401`.

Falha de bootstrap não é fatal para o app: a UI abre num estado "sem identidade", com o
motivo e um botão de tentar de novo.

### Signaling

Cliente WebSocket para `GET /ws/signaling?sessionId={uuid}` com o token no header. O
envelope é o documentado: o app envia apenas `type`, `to`, `payload` e opcionalmente
`messageId`; nunca confia em `sessionId`, `from` ou `timestamp` que ele mesmo tenha
escrito.

Tipos consumidos: `session.joined`, `session.left`, `session.ended`,
`participant.disconnected`, `participant.reconnected`, `participant.capabilities`, `error`.
Tipos enviados: `publisher.ready`, `viewer.ready`, `webrtc.offer`, `webrtc.answer`,
`webrtc.ice_candidate`, `webrtc.renegotiate`, `ping`/`pong`.

Reconexão segue o período de graça do servidor: `participant.disconnected` significa "espere",
não "derrube a peer connection"; `participant.reconnected` significa "retome, com ICE restart
ou renegociação", não "comece do zero". `session.ended`, socket fechado sem nenhuma dessas
mensagens, e `404`/`410` no HTTP são terminais e param as tentativas.

### Máquina de estados

Um `SessionRuntime` só, cobrindo os dois papéis:

```text
Idle ─┬─ Preparando ── Compartilhando ── Encerrando ── Idle
      └─ Entrando ──── Assistindo ────── Encerrando ── Idle
                                      └─ Falhou ───── Idle
```

Trocar de papel passa obrigatoriamente por `Idle`. Todo estado exposto à UI sai deste objeto,
que é também a única fonte da página de Diagnóstico — nenhuma tela mantém estado paralelo.

### Como a Fase 1 se prova

Duas máquinas: uma cria a sessão e mostra o código, a outra entra com o código; as duas
veem uma a outra na lista de participantes e o WebSocket permanece aberto. Nenhum pixel
trafega ainda. É deliberado: identidade, pareamento e signaling falham de formas muito mais
fáceis de diagnosticar sem um pipeline de vídeo por cima.

---

## Fase 2 — Publicar vídeo

### Pipeline

```text
Windows.Graphics.Capture ── textura GPU ── escala/conversão ── encoder H.264 ── amostra
                                                                                   │
                                    ┌──────────────────────────────────────────────┤
                                    ▼                    ▼                         ▼
                              peer viewer 1        peer viewer 2             peer viewer N
```

Captura, escala e encode acontecem **uma vez**, qualquer que seja o número de espectadores.
A amostra codificada é entregue a todas as peer connections. Um encoder por viewer
multiplicaria o custo de GPU pelo número de espectadores.

### Encoder

`SIPSorceryMedia.FFmpeg`, escolhendo em tempo de execução: `h264_nvenc` → `h264_qsv` →
`h264_amf` → `libx264`. A escolha efetiva e o motivo da recusa de cada candidato aparecem no
Diagnóstico — quando alguém reclamar de CPU alta, essa é a primeira pergunta.

Alvos iniciais: até 1080p, 30 fps, 4 Mbps, GOP longo com keyframe sob demanda ao receber PLI.
Conteúdo de tela é majoritariamente estático; forçar keyframes periódicos gastaria banda à toa.

O encoder fica atrás de um `IVideoEncoder` do próprio projeto, não do SIPSorcery. Trocar a
implementação é uma classe nova.

### Negociação

O publisher inicia. Para cada `session.joined` de viewer, cria a peer connection, adiciona o
track de vídeo e envia `webrtc.offer` endereçada ao `participantId` daquele viewer. Servidores
ICE vêm de `GET /api/webrtc/ice-servers`. Mudança de resolução do monitor durante a sessão
dispara `webrtc.renegotiate`.

### Qualidade

Um alvo global por sessão, guiado pelo pior viewer: perda sustentada no RTCP de qualquer um
reduz o alvo para todos, com recuperação gradual. Simulcast e SVC ficam para outra fase.

---

## Fase 3 — Assistir

Ao receber `publisher.ready`, o viewer aprende o `participantId` do publisher pelo campo
autenticado `from` e responde `viewer.ready`. Aplica a offer, gera a answer, troca ICE.

Decode H.264 pelo FFmpeg, com hardware quando disponível. Os quadros decodificados vão para
um controle Avalonia dedicado que escreve num `WriteableBitmap` de tamanho fixo, reciclado —
alocar um bitmap por quadro a 30 fps seria pressão de GC gratuita.

A imagem preserva o aspecto original com letterbox; nunca distorce. Tela cheia e voltar são
a mesma tecla (`F11`, e `Esc` sai).

Estados visíveis para o espectador: conectando, negociando, recebendo, reconectando,
encerrada pelo publisher, falhou. "Sem quadro há mais de N segundos" é um estado distinto de
"desconectado" — a peer connection pode estar viva e a mídia parada, e confundir os dois
manda o usuário depurar a coisa errada.

---

## Fase 4 — Áudio, Diagnóstico, Configurações, instalador

**Áudio.** Loopback WASAPI do endpoint de renderização padrão, codificado em Opus, publicado
como segundo track na mesma peer connection. Uma direção só: quem assiste ouve, não fala.
Silêncio prolongado na captura é reportado como estado, não como erro.

**Diagnóstico.** Console técnico com o que a Fase 2 e a 3 medem: encoder escolhido, fps,
bitrate, resolução, RTT, jitter, perda, direto vs. relay, estado ICE. Exportação de relatório
com redação — nunca SDP completo, candidatos ICE, nome de máquina ou identificador de device.

**Configurações.** Endereço do backend, nome do device, qualidade alvo, forçar relay,
iniciar com o Windows.

**Instalador.** WiX **fixado na v5** — a v6 mudou o EULA do OSMF. Empacota o app e os
binários do FFmpeg 8.1.

---

## Erros

| Situação | Comportamento |
|---|---|
| Bootstrap falha | UI em "sem identidade", com motivo e nova tentativa manual |
| `rotatedCredentialSecret` na renovação | Substitui id e segredo antes de qualquer outra chamada |
| Código inválido, expirado ou sessão encerrada | Mesma mensagem para todos os casos, sem revelar qual |
| `403 device_type_not_allowed` | "Esta sessão só aceita computadores Windows com o SonicDesktopRelay" |
| `409` limite de espectadores | "A sessão já está cheia" |
| Nenhum encoder de hardware | Cai para `libx264`, avisa no Diagnóstico, não bloqueia |
| Falha de captura (troca de sessão de desktop) | Recria a captura mantendo a sessão e as peer connections |
| `participant.disconnected` | Mantém a peer connection e espera o período de graça |
| `session.ended` | Terminal; encerra tudo e volta para `Idle` sem retentar |

## Testes

Cada fase entrega testes que rodam sem hardware:

- **Core/ApiClient/Signaling:** unitários com `HttpMessageHandler` e WebSocket falsos,
  incluindo o caminho de `rotatedCredentialSecret` e o de reconexão dentro do período de graça.
- **Presentation:** a máquina de estados e as projeções, sem UI.
- **Rtc:** fan-out para N peers e renegociação, com encoder falso — o pipeline inteiro se
  prova sem GPU.
- **Media.Windows:** os adaptadores, marcados para rodar só onde há Windows e hardware.
- **Fase 0:** testes de integração da API cobrindo o novo tipo, o novo modo, o auto-pareamento
  escopado, a recusa por tipo de device e — o mais importante — que `broadcast` e `duplex`
  respondem exatamente como antes.

## Riscos

| Risco | Impacto | Mitigação |
|---|---|---|
| Empacotar FFmpeg 8.1 se mostrar frágil | Alto | Costura `IVideoEncoder`; VP8 gerenciado é o plano B |
| Encoder de hardware instável entre GPUs | Médio | Cadeia de fallback até `libx264`, escolha visível no Diagnóstico |
| Código de 6 caracteres como única credencial | Médio | TTL curto, rotação, lista de espectadores sempre visível |
| Banda de vídeo no coturn | Alto | P2P preferencial, teto de bitrate, métricas direto vs. relay |
| Regressão nos apps existentes | Alto | Tipo e modo novos em vez de ampliar os existentes; testes de não-regressão na Fase 0 |
| Render em Avalonia não sustentar 30 fps | Médio | `WriteableBitmap` reciclado; medido na Fase 3 antes de seguir |
