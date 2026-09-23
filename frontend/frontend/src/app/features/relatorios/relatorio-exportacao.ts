import { jsPDF } from 'jspdf';
import autoTable, { CellHookData } from 'jspdf-autotable';
import * as XLSX from 'xlsx';
import { ReportRow, ReportTurma } from '../../core/models/models';
import { rotuloTurno } from '../../core/utils/turma';

export interface DadosRelatorio {
  alunos: ReportRow[];
  /** Busca aplicada na tela: o arquivo avisa que é um recorte. */
  busca: string;
}

type Valores = Pick<ReportTurma,
  'hours' | 'required' | 'progressPercent' | 'approved' | 'irregular' | 'pendencyDays' | 'pendencyHours'>;

/** Uma linha exportada: a única turma do aluno, cada turma de quem cursa várias, ou o total. */
interface Linha {
  tipo: 'unica' | 'turma' | 'total';
  primeira: boolean;
  aluno: ReportRow;
  turma?: ReportTurma;
  valores: Valores;
}

const AZUL_ESCURO: [number, number, number] = [11, 66, 122];
const AZUL: [number, number, number] = [0, 173, 238];
const CINZA: [number, number, number] = [107, 114, 128];
const VERMELHO: [number, number, number] = [185, 28, 28];
const VERDE: [number, number, number] = [21, 128, 61];

const TITULO = 'Relatório de carga horária e pendências';
const INSTITUICAO = 'UDF Centro Universitário';
const CURSO = 'Curso de Enfermagem · Estágio Supervisionado';

const num = (valor: number, casas = 1): string =>
  valor.toLocaleString('pt-BR', { maximumFractionDigits: casas });

/** Emissão no horário de Brasília, independente do fuso do computador. */
function emissao(): { data: string; hora: string; arquivo: string } {
  const agora = new Date();
  const fuso = { timeZone: 'America/Sao_Paulo' } as const;
  return {
    data: agora.toLocaleDateString('pt-BR', fuso),
    hora: agora.toLocaleTimeString('pt-BR', { ...fuso, hour: '2-digit', minute: '2-digit' }),
    arquivo: new Intl.DateTimeFormat('sv-SE', fuso).format(agora)
  };
}

function linhas(alunos: ReportRow[]): Linha[] {
  return alunos.flatMap((a): Linha[] => a.turmas.length <= 1
    ? [{ tipo: 'unica', primeira: true, aluno: a, turma: a.turmas[0], valores: a }]
    : [
        ...a.turmas.map((t, i): Linha => ({ tipo: 'turma', primeira: i === 0, aluno: a, turma: t, valores: t })),
        { tipo: 'total', primeira: false, aluno: a, valores: a }
      ]);
}

function resumo(alunos: ReportRow[]) {
  return {
    alunos: alunos.length,
    liberados: alunos.filter(a => a.certificateReleased).length,
    horas: alunos.reduce((s, a) => s + a.hours, 0),
    comPendencia: alunos.filter(a => a.pendencyDays > 0).length
  };
}

// ── PDF ─────────────────────────────────────────────────────────────────────

/** A4 paisagem com cabeçalho institucional, cartões de resumo e a tabela por aluno. */
export function gerarPdf(dados: DadosRelatorio, logo: string | null): void {
  const { doc, nome } = montarPdf(dados, logo);
  doc.save(nome);
}

export function montarPdf({ alunos, busca }: DadosRelatorio, logo: string | null): { doc: jsPDF; nome: string } {
  const doc = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4' });
  const largura = doc.internal.pageSize.getWidth();
  const altura = doc.internal.pageSize.getHeight();
  const margem = 12;
  const quando = emissao();

  // Cabeçalho
  if (logo) doc.addImage(logo, 'PNG', margem, 9, 15, 15);
  const xTexto = logo ? margem + 19 : margem;
  doc.setFont('helvetica', 'bold').setFontSize(15).setTextColor(...AZUL_ESCURO);
  doc.text(INSTITUICAO, xTexto, 15.5);
  doc.setFont('helvetica', 'normal').setFontSize(9).setTextColor(...CINZA);
  doc.text(CURSO, xTexto, 21);

  doc.setFontSize(8).text('Data de emissão', largura - margem, 12, { align: 'right' });
  doc.setFont('helvetica', 'bold').setFontSize(11).setTextColor(...AZUL_ESCURO);
  doc.text(`${quando.data} às ${quando.hora}`, largura - margem, 17.5, { align: 'right' });
  doc.setFont('helvetica', 'normal').setFontSize(8).setTextColor(...CINZA);
  doc.text('Horário de Brasília', largura - margem, 22, { align: 'right' });

  doc.setDrawColor(...AZUL).setLineWidth(0.9).line(margem, 27, largura - margem, 27);

  // Título
  doc.setFont('helvetica', 'bold').setFontSize(16).setTextColor(...AZUL_ESCURO);
  doc.text(TITULO, margem, 36);
  doc.setFont('helvetica', 'normal').setFontSize(9).setTextColor(75, 85, 99);
  doc.text('Horas aprovadas, registros e dias sem registro de cada aluno. Quem cursa mais de uma turma '
         + 'aparece com o detalhe de cada uma e o total, que decide a liberação do certificado.',
           margem, 41.5, { maxWidth: largura - 2 * margem });

  let y = 47;
  if (busca) {
    const texto = `Filtrado por: "${busca}"`;
    doc.setFontSize(8);
    const w = doc.getTextWidth(texto) + 6;
    doc.setFillColor(254, 249, 195).roundedRect(margem, y - 3.6, w, 5.2, 2.5, 2.5, 'F');
    doc.setTextColor(133, 77, 14).text(texto, margem + 3, y);
    y += 5;
  }

  // Cartões de resumo
  const r = resumo(alunos);
  const cartoes: [string, string][] = [
    [`${r.alunos}`, 'ALUNOS'],
    [`${r.liberados}`, 'CERTIFICADOS LIBERADOS'],
    [`${num(r.horas, 0)} h`, 'HORAS APROVADAS'],
    [`${r.comPendencia}`, 'ALUNOS COM PENDÊNCIAS']
  ];
  const gap = 4;
  const wCartao = (largura - 2 * margem - gap * 3) / 4;
  cartoes.forEach(([valor, rotulo], i) => {
    const x = margem + i * (wCartao + gap);
    doc.setFillColor(248, 251, 255).setDrawColor(219, 234, 254).setLineWidth(0.3);
    doc.roundedRect(x, y, wCartao, 15, 1.5, 1.5, 'FD');
    doc.setFillColor(...AZUL).rect(x, y, 1.2, 15, 'F');
    doc.setFont('helvetica', 'bold').setFontSize(14).setTextColor(...AZUL_ESCURO).text(valor, x + 5, y + 7);
    doc.setFont('helvetica', 'normal').setFontSize(7).setTextColor(...CINZA).text(rotulo, x + 5, y + 12);
  });
  y += 21;

  // Tabela
  const dados = linhas(alunos);
  const COL_PROGRESSO = 3;
  const COL_IRREGULARES = 5;
  const COL_PENDENCIAS = 6;
  const COL_CERTIFICADO = 7;
  const subtitulos = new Map<string, string>();

  autoTable(doc, {
    startY: y,
    margin: { left: margem, right: margem, top: 14, bottom: 14 },
    head: [['Aluno', 'Turma', 'Horas', 'Progresso', 'Aprovados', 'Irregulares', 'Pendências', 'Certificado']],
    body: dados.map(l => [
      l.primeira ? `${l.aluno.fullName}${l.aluno.isActive ? '' : ' (inativo)'}\nRGM ${l.aluno.rgm || '—'}` : '',
      l.tipo === 'total'
        ? `Total\n${l.aluno.turmas.length} turmas`
        : l.turma ? `${l.turma.groupCode}\n${rotuloTurno(l.turma.shift)}` : '—',
      `${num(l.valores.hours)} h / ${num(l.valores.required, 0)} h`,
      `${num(l.valores.progressPercent, 0)}%`,
      `${l.valores.approved}`,
      `${l.valores.irregular}`,
      `${l.valores.pendencyDays} ${l.valores.pendencyDays === 1 ? 'dia' : 'dias'}`,
      l.tipo === 'turma' ? '' : (l.aluno.certificateReleased ? 'Liberado' : 'Pendente')
    ]),
    showHead: 'everyPage',
    theme: 'plain',
    styles: { font: 'helvetica', fontSize: 8.5, cellPadding: { top: 2.2, bottom: 2.2, left: 2.5, right: 2.5 },
              textColor: [10, 10, 10], lineColor: [229, 231, 235], valign: 'middle' },
    headStyles: { fillColor: AZUL_ESCURO, textColor: 255, fontStyle: 'bold', fontSize: 8 },
    columnStyles: {
      0: { cellWidth: 70 },
      1: { cellWidth: 32 },
      2: { halign: 'right', cellWidth: 34 },
      3: { cellWidth: 44 },
      4: { halign: 'right' },
      5: { halign: 'right' },
      6: { halign: 'right' },
      7: { halign: 'center', cellWidth: 28 }
    },
    didParseCell: (c: CellHookData) => {
      if (c.section !== 'body') return;
      const l = dados[c.row.index];
      c.cell.styles.lineWidth = { top: l.primeira ? 0.3 : 0, bottom: 0, left: 0, right: 0 };
      if (l.tipo === 'total') { c.cell.styles.fillColor = [241, 245, 249]; c.cell.styles.fontStyle = 'bold'; }
      if (c.column.index === 0 && l.primeira) c.cell.styles.fontStyle = 'bold';
      if ((c.column.index === COL_IRREGULARES && l.valores.irregular > 0)
          || (c.column.index === COL_PENDENCIAS && l.valores.pendencyDays > 0)) {
        c.cell.styles.textColor = VERMELHO;
        c.cell.styles.fontStyle = 'bold';
      }
      // Barra e selo são desenhados à mão; o texto da célula só reserva o espaço.
      if (c.column.index === COL_PROGRESSO || c.column.index === COL_CERTIFICADO) c.cell.text = [''];
      // A segunda linha (RGM, turno) é redesenhada em cinza; aqui ela só reserva a altura.
      if (c.column.index <= 1 && c.cell.text.length === 2) {
        subtitulos.set(`${c.row.index}:${c.column.index}`, c.cell.text[1]);
        c.cell.text = [c.cell.text[0], ''];
      }
    },
    didDrawCell: (c: CellHookData) => {
      if (c.section !== 'body') return;
      const l = dados[c.row.index];
      const meioY = c.cell.y + c.cell.height / 2;

      const sub = subtitulos.get(`${c.row.index}:${c.column.index}`);
      if (sub) {
        const linha = (c.cell.styles.fontSize * doc.getLineHeightFactor()) / doc.internal.scaleFactor;
        const topo = c.cell.y + (c.cell.height - 2 * linha) / 2;
        doc.setFont('helvetica', 'normal').setFontSize(7.5).setTextColor(...CINZA);
        doc.text(sub, c.cell.x + 2.5, topo + linha * 1.75);
      }

      if (c.column.index === COL_PROGRESSO) {
        const pct = Math.max(0, Math.min(100, l.valores.progressPercent));
        const trilho = c.cell.width - 18;
        const x = c.cell.x + 2.5;
        doc.setFillColor(229, 231, 235).roundedRect(x, meioY - 1, trilho, 2, 1, 1, 'F');
        if (pct > 0) doc.setFillColor(...(pct >= 100 ? VERDE : AZUL)).roundedRect(x, meioY - 1, trilho * pct / 100, 2, 1, 1, 'F');
        doc.setFont('helvetica', 'normal').setFontSize(8).setTextColor(10, 10, 10);
        doc.text(`${num(l.valores.progressPercent, 0)}%`, c.cell.x + c.cell.width - 2.5, meioY + 1.1, { align: 'right' });
      }

      if (c.column.index === COL_CERTIFICADO && l.tipo !== 'turma') {
        const liberado = l.aluno.certificateReleased;
        const texto = liberado ? 'Liberado' : 'Pendente';
        doc.setFont('helvetica', 'bold').setFontSize(7.5);
        const w = doc.getTextWidth(texto) + 6;
        const x = c.cell.x + (c.cell.width - w) / 2;
        doc.setFillColor(...(liberado ? [220, 252, 231] as [number, number, number] : [254, 226, 226] as [number, number, number]));
        doc.roundedRect(x, meioY - 2.4, w, 4.8, 2.4, 2.4, 'F');
        doc.setTextColor(...(liberado ? [22, 101, 52] as [number, number, number] : [153, 27, 27] as [number, number, number]));
        doc.text(texto, x + 3, meioY + 1);
      }
    }
  });

  if (!alunos.length) {
    doc.setFontSize(10).setTextColor(...CINZA).text('Nenhum aluno para exibir.', largura / 2, y + 20, { align: 'center' });
  }

  // Rodapé em todas as páginas
  const paginas = doc.getNumberOfPages();
  for (let p = 1; p <= paginas; p++) {
    doc.setPage(p);
    doc.setDrawColor(229, 231, 235).setLineWidth(0.2).line(margem, altura - 10, largura - margem, altura - 10);
    doc.setFont('helvetica', 'normal').setFontSize(7.5).setTextColor(...CINZA);
    doc.text(`EstágioCheck · ${TITULO} · emitido em ${quando.data} às ${quando.hora}`, margem, altura - 6);
    doc.text(`Página ${p} de ${paginas}`, largura - margem, altura - 6, { align: 'right' });
  }

  return { doc, nome: `relatorio-carga-horaria-${quando.arquivo}.pdf` };
}

// ── Excel ───────────────────────────────────────────────────────────────────

/** Planilha com cabeçalho institucional, filtro nas colunas e uma aba de resumo. */
export function gerarXlsx(dados: DadosRelatorio): void {
  const { livro, nome } = montarXlsx(dados);
  XLSX.writeFile(livro, nome);
}

export function montarXlsx({ alunos, busca }: DadosRelatorio): { livro: XLSX.WorkBook; nome: string } {
  const quando = emissao();
  const cabecalhoTabela = ['Aluno', 'RGM', 'Situação', 'Turma', 'Turno', 'Horas aprovadas', 'Horas exigidas',
    'Progresso (%)', 'Aprovados', 'Irregulares', 'Dias pendentes', 'Horas pendentes', 'Certificado'];

  const topo: (string | number)[][] = [
    [`${INSTITUICAO} — ${TITULO}`],
    [CURSO],
    [`Data de emissão: ${quando.data} às ${quando.hora} (horário de Brasília)`],
    ...(busca ? [[`Filtrado por: "${busca}"`]] : []),
    []
  ];

  const corpo = linhas(alunos).map(l => [
    l.aluno.fullName,
    l.aluno.rgm ?? '',
    l.aluno.isActive ? 'Ativo' : 'Inativo',
    l.tipo === 'total' ? `TOTAL (${l.aluno.turmas.length} turmas)` : (l.turma?.groupCode ?? ''),
    l.tipo === 'total' ? '' : rotuloTurno(l.turma?.shift),
    l.valores.hours,
    l.valores.required,
    Math.round(l.valores.progressPercent),
    l.valores.approved,
    l.valores.irregular,
    l.valores.pendencyDays,
    l.valores.pendencyHours,
    l.tipo === 'turma' ? '' : (l.aluno.certificateReleased ? 'Liberado' : 'Pendente')
  ]);

  const planilha = XLSX.utils.aoa_to_sheet([...topo, cabecalhoTabela, ...corpo]);
  const linhaCabecalho = topo.length;
  const ultimaColuna = cabecalhoTabela.length - 1;

  planilha['!merges'] = topo.slice(0, -1).map((_, i) => ({ s: { r: i, c: 0 }, e: { r: i, c: ultimaColuna } }));
  planilha['!autofilter'] = {
    ref: XLSX.utils.encode_range({ s: { r: linhaCabecalho, c: 0 }, e: { r: linhaCabecalho + corpo.length, c: ultimaColuna } })
  };
  planilha['!cols'] = [34, 12, 10, 18, 10, 15, 14, 13, 11, 11, 14, 15, 13].map(wch => ({ wch }));

  const r = resumo(alunos);
  const abaResumo = XLSX.utils.aoa_to_sheet([
    [`${INSTITUICAO} — ${TITULO}`],
    [`Data de emissão: ${quando.data} às ${quando.hora} (horário de Brasília)`],
    [],
    ['Indicador', 'Valor'],
    ['Alunos', r.alunos],
    ['Certificados liberados', r.liberados],
    ['Horas aprovadas', Math.round(r.horas * 10) / 10],
    ['Alunos com pendências', r.comPendencia]
  ]);
  abaResumo['!cols'] = [{ wch: 28 }, { wch: 14 }];

  const livro = XLSX.utils.book_new();
  livro.Props = { Title: TITULO, Author: 'EstágioCheck', Company: INSTITUICAO };
  XLSX.utils.book_append_sheet(livro, planilha, 'Relatório');
  XLSX.utils.book_append_sheet(livro, abaResumo, 'Resumo');
  return { livro, nome: `relatorio-carga-horaria-${quando.arquivo}.xlsx` };
}
