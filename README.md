# FatePilot 0.1

**By Mokoena**

Trace sua rota. Encontre seu destino.

Plugin independente em C# para Dalamud API 15. Interface própria em tons de ardósia e dourado, controles compactos, logo local e painel de sessão. Esta é uma **compilação experimental para testes**, ainda sem validação dentro do jogo.

## O que está implementado

- Seleção de FATEs de combate já iniciados, com filtros de nível, progresso e tempo restante; preferência por bônus e lista de ignorados.
- Deslocamento, montaria, voo onde desbloqueado e sincronização de nível.
- Rota ordenada de zonas e rotação das instâncias conhecidas antes de seguir para a próxima zona.
- Retorno à cidade após X sucessos observados. Meta zero mantém o ciclo até Parar.
- Aetheryte Tickets: ligar/desligar, tarifa mínima e escolha explícita entre parar ou pagar gil quando acabarem.
- Limite de gil por sessão, incluindo o retorno. Custos aceitos são reservados mesmo se o teleporte for cancelado depois.
- Detecção de ausência de progresso, prazos máximos, interrupção em morte, desconexão, mudança de classe, entrada em duty/PvP e conflito de controle.
- Diagnóstico local, recuperação de ajustes após interrupção e botão compacto de parada.
- UI em inglês, japonês, alemão, francês, português **do Brasil** e espanhol. Auto acompanha o idioma oficial do cliente; PT-BR e ES são escolhas manuais salvas.
- Sobre com logo, “By Mokoena” e botão “Buy my cat a treat”. O link só é aberto pelo clique.

FATEs de coleta, escolta e ativação por NPC não são selecionados nesta versão. A contagem usa participação em combate e conclusão observada, sem ler o pacote de recompensa: eventos que desaparecem sem confirmação não contam para a meta.

## Instalação pelo Dalamud

Em **Dalamud Settings → Experimental → Custom Plugin Repositories**, adicione e habilite:

```text
https://raw.githubusercontent.com/danil-mokoena/FatePilot/main/pluginmaster.json
```

Salve, abra `/xlplugins`, procure **FatePilot** e instale. Confira antes as [integrações necessárias](INTEGRATIONS.md). O catálogo é público e aponta para o pacote na Release do GitHub. A 0.1 é experimental; publicação não significa validação em execução. O mesmo endereço do catálogo será usado para as próximas atualizações.

## Instalação local para desenvolvimento

1. Extraia `FatePilot-0.1.0-plugin.zip` em uma pasta permanente. Mantenha `FatePilot.dll`, `FatePilot.Core.dll`, `FatePilot.deps.json` e `FatePilot.json` juntos.
2. Confira as integrações em [INTEGRATIONS.md](INTEGRATIONS.md). Os componentes já instalados no jogo são usados por suas APIs locais; suas DLLs não estão neste pacote.
3. Nas configurações do Dalamud (`/xlsettings`), abra a área de desenvolvimento/Experimental e adicione o caminho completo de `FatePilot.dll` em **Dev Plugin Locations**. Os rótulos podem variar conforme a versão do host.
4. Na lista de plugins (`/xlplugins`), carregue o plugin de desenvolvimento FatePilot. Se necessário, atualize a lista após adicionar o caminho.
5. Abra `/fatepilot`. Na aba de sessão, escolha o idioma e a cidade de retorno. Na rota, escolha um aetheryte por zona. Em diagnóstico, verifique os quatro módulos.
6. Pare as outras automações antes de iniciar. Para a primeira validação, use uma zona conhecida e meta de um FATE. Acompanhe a sessão.

O pacote não ativa uma sessão de farming ao carregar. A DLL deve permanecer com a mesma versão de API do Dalamud usada na compilação.

## Tickets e instâncias

Abra a janela de Teleport uma vez antes do primeiro teste com tickets. O plugin usa as funções nativas de Teleport e de ticket separadamente; não aceita diálogos genéricos de Sim/Não. O fluxo exato de confirmação e consumo precisa ser validado com as suas opções de Teleport. Ao chegar, ele verifica o consumo; inconsistência interrompe a sessão. Essa verificação não desfaz um pagamento que o cliente já tenha feito.

Para as instâncias, abra uma vez o menu de troca no aetheryte para que o módulo de viagens descubra as opções e habilite o recurso indicado em [INTEGRATIONS.md](INTEGRATIONS.md). Sem informação confirmada, o FatePilot não inventa um número de instâncias.

`/fatepilot stop` encerra a sessão, cancela as rotas do plugin, libera o controle temporário e tenta cancelar o seu Teleport se ele ainda estiver sendo conjurado. Uma transição já aceita pelo jogo pode terminar depois da parada.

## Comandos

| Comando | Ação |
|---|---|
| `/fatepilot` | Abrir o painel |
| `/fatepilot start` | Iniciar com as opções salvas |
| `/fatepilot stop` | Parar e restaurar os ajustes anteriores |
| `/fatepilot status` | Mostrar estado no chat local |
| `/fatepilot recover` | Restaurar o registro local da sessão anterior, quando parado |

## Validação e próximos passos

Compilação Release e 40 testes locais aprovados. Os testes cobrem seleção, interrupções, rotação de zonas/instâncias simulada, contagem de sucesso, orçamento, tickets, traduções e inspeção de capacidades das DLLs. Não substituem o teste das APIs nativas em execução. Traduções completas, ainda sem revisão por falantes nativos de todos os idiomas.

O roteiro de teste dentro do jogo está em [VALIDATION.md](VALIDATION.md). Segurança e armazenamento estão descritos em [SECURITY.md](SECURITY.md).

Perfis, histórico persistente, fila de classes, reparo, consumíveis, compras de vouchers, modo de grupo e paradas por tempo/inventário/equipamento **não foram implementados nesta 0.1**. Não há recursos de detecção de GM ou tentativa de disfarçar a automação.

## Compilar

Requer .NET SDK 10 e referências locais do Dalamud API 15. Execute `./build.ps1` no PowerShell; para um SDK fora do PATH, use `./build.ps1 -Dotnet 'C:/caminho/dotnet.exe'`. O script compila, executa os testes, monta os pacotes e gera SHA-256. O SDK oficial e o empacotador estão fixados na versão 15.0.0. A restauração de ferramentas pode acessar os serviços de pacotes durante a compilação; isso não ocorre no código de execução do plugin.

`src/FatePilot.Core` contém a lógica independente do jogo; `src/FatePilot` contém UI e adaptadores; `tests` contém testes e verificações de privacidade. `IntegrationIds.cs` reúne os nomes exatos dos contratos externos necessários à interoperabilidade.

Para preparar o catálogo de uma Release, execute `./prepare-repository.ps1 -ReleaseDirectory './artifacts'`. O script verifica a versão da DLL e do manifesto antes de escrever `pluginmaster.json`. Publique primeiro o ZIP da Release e depois a atualização do catálogo. O script não acessa sua conta nem publica arquivos sozinho.
