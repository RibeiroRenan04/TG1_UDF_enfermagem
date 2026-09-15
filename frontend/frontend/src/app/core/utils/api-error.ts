import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, FormGroup } from '@angular/forms';

/**
 * Corpo de erro padronizado pela API: `{ message, field?, code?, errors? }`.
 * `errors` mapeia o nome do campo para as mensagens daquele campo.
 */
interface CorpoErroApi {
  message?: string;
  title?: string;
  field?: string;
  code?: string;
  errors?: Record<string, string[] | string> | null;
}

/** Texto para quando o servidor não explica o motivo — ao menos o status diz algo. */
function mensagemPorStatus(status: number): string {
  if (status === 0) return 'Sem conexão com o servidor. Verifique sua internet e tente novamente.';
  if (status === 400) return 'Os dados enviados são inválidos. Revise os campos destacados.';
  if (status === 401) return 'Sua sessão expirou. Entre novamente.';
  if (status === 403) return 'Você não tem permissão para realizar esta ação.';
  if (status === 404) return 'O registro não foi encontrado. Ele pode ter sido removido — recarregue a tela.';
  if (status === 409) return 'Conflito com um registro existente (por exemplo, agenda ou turno já ocupado).';
  if (status === 413) return 'O arquivo enviado é grande demais.';
  if (status >= 500) return 'Erro interno do servidor. Tente novamente em instantes.';
  return 'Não foi possível concluir a operação.';
}

function corpo(err: unknown): CorpoErroApi | null {
  const body = (err as HttpErrorResponse)?.error;
  if (!body) return null;
  if (typeof body === 'string') {
    try { return JSON.parse(body); } catch { return { message: body }; }
  }
  return typeof body === 'object' ? body as CorpoErroApi : null;
}

function listaErros(c: CorpoErroApi | null): [string, string[]][] {
  if (!c?.errors) return [];
  return Object.entries(c.errors).map(([campo, msgs]) =>
    [campo, Array.isArray(msgs) ? msgs : [msgs]] as [string, string[]]);
}

/**
 * Motivo real do erro, para o aviso da tela.
 *
 * Com explicação do servidor, `contexto` do tipo "Erro ao salvar a atividade."
 * vira prefixo: "Erro ao salvar a atividade: O horário de término precisa ser
 * posterior ao de início." Sem explicação, o status HTTP define o texto — nunca
 * mais um "Erro ao salvar" que não diz o que corrigir.
 */
export function mensagemErro(err: unknown, contexto: string): string {
  const c = corpo(err);
  const status = (err as HttpErrorResponse)?.status ?? -1;

  const detalhe = c?.message?.trim()
    || listaErros(c).flatMap(([, m]) => m).slice(0, 3).join(' ')
    || (status >= 0 ? mensagemPorStatus(status) : '');

  if (!detalhe) return contexto;
  // Frases de contexto afirmativas ("Aluno não encontrado para esse RGM.") já são
  // o próprio aviso; só "Erro ao …" ganha o motivo como complemento.
  if (!/^erro ao /i.test(contexto)) return c?.message?.trim() || contexto;

  return `${contexto.replace(/[.!\s]+$/, '')}: ${detalhe}`;
}

function controlePorNome(form: FormGroup, campo: string): AbstractControl | null {
  // "days[0].mode" não existe no formulário; tenta o caminho e depois o nome simples.
  const alvo = campo.replace(/\[\d+\]/g, '').split('.').pop() ?? campo;
  const nomes = Object.keys(form.controls);
  const nome = nomes.find(n => n === campo)
    ?? nomes.find(n => n.toLowerCase() === campo.toLowerCase())
    ?? nomes.find(n => n.toLowerCase() === alvo.toLowerCase());
  return nome ? form.controls[nome] : null;
}

/**
 * Marca no formulário os campos que o servidor recusou: o campo fica com a borda
 * vermelha do Material e o texto do erro (`servidor`) aparece logo abaixo. O erro
 * some sozinho quando o usuário altera o valor (a validação roda de novo).
 *
 * @returns quantos campos foram destacados.
 */
export function aplicarErrosServidor(form: FormGroup, err: unknown): number {
  const c = corpo(err);
  const erros = listaErros(c);
  if (c?.field && c.message && !erros.some(([campo]) => campo === c.field)) {
    erros.push([c.field, [c.message]]);
  }

  let aplicados = 0;
  for (const [campo, msgs] of erros) {
    const controle = controlePorNome(form, campo);
    if (!controle || !msgs.length) continue;
    controle.setErrors({ ...(controle.errors ?? {}), servidor: msgs[0] });
    controle.markAsTouched();
    aplicados++;
  }
  return aplicados;
}
