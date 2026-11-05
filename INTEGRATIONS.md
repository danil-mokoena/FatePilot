# Integrações técnicas

Este arquivo identifica os componentes necessários para instalar e depurar o plugin. Os nomes são contratos de interoperabilidade; não são créditos de origem nem marcas exibidas pela UI.

| Papel no FatePilot | Componente | Interface utilizada |
|---|---|---|
| Habilidades | WrathCombo | Lease temporário, configuração e ativação de habilidades; liberação ao encerrar |
| Posicionamento | BossMod original | Configuração, presets de movimento e mapa temporário de obstáculos |
| Navegação | vnavmesh | PathfindCancelable, MoveTo, Stop e consulta de ponto navegável |
| Instâncias | Lifestream | ChangeInstance, IsBusy, Abort, CanChangeInstance e descoberta de instâncias |

As referências locais consultadas incluíam BossMod 7.5.7.20, WrathCombo 1.0.4.27 e vnavmesh 1.2.3.14. Não equivale a certificação de compatibilidade em execução. A aba Diagnóstico verifica a presença das APIs antes de iniciar. Atualizações que alterem contratos podem exigir recompilar o adaptador.

No módulo de viagens, habilite **Instance Switcher** e abra o menu de troca no aetheryte para descobrir as instâncias. O teleporte entre zonas e o consumo de tickets são solicitados diretamente pelas funções do cliente, via FFXIVClientStructs.

Não carregue simultaneamente variantes do módulo de posicionamento que registrem os mesmos canais de IPC. O FatePilot rejeita esse cenário para evitar controlar o componente errado. As outras automações de combate, navegação e viagem devem estar paradas.

O plugin cria um preset temporário que contém apenas módulos de movimento, salva o estado anterior e desativa o controle de seguimento durante a sessão. As habilidades ficam sob um lease separado. Ao encerrar, ele libera esse lease, restaura o estado anterior e remove seu preset. Se outro componente alterar o controle durante a sessão, ele para. Presets alterados externamente são preservados na restauração normal.

Esses componentes continuam sendo programas independentes. Por exemplo, o módulo de habilidades consulta remotamente o estado de sua interface, e o de navegação pode baixar dados de navegação. A ausência de um cliente HTTP no FatePilot não transforma todas as dependências em software offline. Este pacote não altera essas dependências.

Não são distribuídas implementações, DLLs ou recursos dessas integrações; elas permanecem sob suas licenças e instalações próprias.
