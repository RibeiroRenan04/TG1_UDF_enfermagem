namespace EstagioCheck.API.Services.Privacidade;

/// <summary>
/// Aviso de privacidade (LGPD art. 9º) servido pelo backend, como o termo de responsabilidade, para
/// todo cliente mostrar a mesma versão. O texto precisa ser validado pelo jurídico/encarregado da UDF.
/// </summary>
public static class AvisoPrivacidade
{
    public const string Versao = "1.0";

    public static object Montar(IConfiguration config) => new
    {
        titulo = "Aviso de Privacidade — EstágioCheck",
        versao = Versao,
        controlador = config["Privacidade:Controlador"] ?? "Centro Universitário do Distrito Federal (UDF)",
        encarregado = new
        {
            nome = config["Privacidade:Encarregado:Nome"] ?? "Encarregado de Proteção de Dados da UDF",
            email = config["Privacidade:Encarregado:Email"] ?? "",
        },
        secoes = new[]
        {
            new
            {
                titulo = "Quais dados tratamos",
                itens = new[]
                {
                    "Cadastro: nome, e-mail, RGM (matrícula), semestre, turno, telefone e vínculo institucional.",
                    "Registro de ponto: data, hora, unidade, descrição das atividades e a localização do aparelho no momento do registro — a localização não é coletada em nenhum outro momento.",
                    "Acompanhamento do estágio: avaliações, acompanhamentos formativos, irregularidades e assinaturas eletrônicas (nome, data e endereço IP).",
                    "Segurança: registros de acesso (data, hora, IP e navegador) e trilha de auditoria das ações realizadas.",
                }
            },
            new
            {
                titulo = "Para que usamos e com qual base legal",
                itens = new[]
                {
                    "Controlar a frequência e a carga horária do estágio curricular obrigatório (Lei 11.788/2008) — cumprimento de obrigação legal e regulatória (LGPD art. 7º, II).",
                    "Avaliar e acompanhar a formação do estudante e emitir o certificado — execução do contrato educacional (art. 7º, V).",
                    "Confirmar a presença do estudante na unidade de saúde pela geolocalização e prevenir fraude no ponto — execução do contrato e legítimo interesse (art. 7º, V e IX).",
                    "Manter registros de acesso e auditoria para segurança da informação — obrigação legal (Marco Civil da Internet, art. 15) e legítimo interesse.",
                }
            },
            new
            {
                titulo = "Com quem compartilhamos",
                itens = new[]
                {
                    "Preceptores, professores e secretaria do estágio, apenas na medida da sua função.",
                    "Operadores de infraestrutura que hospedam o sistema: Supabase (banco de dados), Railway (API), Vercel (site) e o provedor de e-mail usado para o código de recuperação de senha.",
                    "Parte desses provedores mantém servidores fora do Brasil; a transferência internacional segue o art. 33 da LGPD, com cláusulas contratuais de proteção.",
                    "Não vendemos nem cedemos dados pessoais para publicidade.",
                }
            },
            new
            {
                titulo = "Por quanto tempo guardamos",
                itens = new[]
                {
                    "O registro acadêmico do estágio (horas, avaliações e certificado) é guardado pelo prazo exigido para documentos acadêmicos.",
                    "Encerrado o vínculo, os dados que identificam a pessoa podem ser anonimizados, preservando apenas o registro acadêmico.",
                    "Códigos de recuperação de senha são apagados um dia após expirarem; a trilha de auditoria é mantida por até 5 anos.",
                }
            },
            new
            {
                titulo = "Seus direitos",
                itens = new[]
                {
                    "Confirmar o tratamento, acessar e receber uma cópia dos seus dados (disponível no próprio sistema, em \"Meus dados\").",
                    "Corrigir dados incompletos, inexatos ou desatualizados.",
                    "Pedir anonimização, bloqueio ou eliminação de dados desnecessários, e informações sobre o compartilhamento.",
                    "Os pedidos são feitos ao encarregado pelo e-mail informado neste aviso e respondidos em até 15 dias.",
                }
            },
            new
            {
                titulo = "Como protegemos",
                itens = new[]
                {
                    "Tráfego sempre criptografado (HTTPS), senhas guardadas com hash BCrypt e acesso por perfil.",
                    "Bloqueio temporário após tentativas repetidas de acesso e registro de auditoria de todas as alterações.",
                    "Em caso de incidente de segurança com risco relevante, os titulares e a ANPD são comunicados (LGPD art. 48).",
                }
            },
        }
    };
}
