import { Component, Inject, Optional, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { UnidadesSaudeService } from '../../core/services/unidades-saude.service';
import { UnidadeSaude, GeocodificacaoResposta } from '../../core/models/models';

/**
 * Cadastro e edição da unidade de saúde.
 *
 * O botão "Buscar localização" passa pela API do sistema, nunca pelo Nominatim
 * direto: é o backend que controla o limite de uso, o User-Agent e o cache.
 */
@Component({
  selector: 'app-unidade-form-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatIconModule, MatCheckboxModule, MatProgressSpinnerModule
  ],
  templateUrl: './unidade-form-dialog.component.html',
  styleUrls: ['./unidade-form-dialog.component.scss']
})
export class UnidadeFormDialogComponent {
  busy = signal(false);
  buscandoLocal = signal(false);
  erro = signal('');
  previa = signal<GeocodificacaoResposta | null>(null);

  readonly editando: boolean;
  readonly tipos = ['UBS', 'Hospital', 'UPA', 'Policlínica', 'CAPS', 'Instituição de ensino', 'Outro'];

  form = this.fb.group({
    nome:        ['', [Validators.required, Validators.minLength(2), Validators.maxLength(200)]],
    tipo:        ['UBS'],
    endereco:    [''],
    numero:      [''],
    complemento: [''],
    bairro:      [''],
    cidade:      ['Brasília'],
    uf:          ['DF', [Validators.maxLength(2)]],
    cep:         [''],
    telefone:    [''],
    raioMetros:  [150, [Validators.min(10), Validators.max(5000)]],
    inicioTurno: ['07:00'],
    fimTurno:    ['13:00'],
    ehInstituicao: [false],
    latitude:    [null as number | null],
    longitude:   [null as number | null]
  });

  constructor(
    private fb: FormBuilder,
    public dialogRef: MatDialogRef<UnidadeFormDialogComponent>,
    private service: UnidadesSaudeService,
    @Optional() @Inject(MAT_DIALOG_DATA) public data?: { unidade?: UnidadeSaude }
  ) {
    this.editando = !!data?.unidade;
    const u = data?.unidade;
    if (u) {
      this.form.patchValue({
        nome: u.nome, tipo: u.tipo ?? '', endereco: u.endereco ?? '', numero: u.numero ?? '',
        complemento: u.complemento ?? '', bairro: u.bairro ?? '', cidade: u.cidade ?? '',
        uf: u.uf ?? '', cep: u.cep ?? '', telefone: u.telefone ?? '',
        raioMetros: u.raioMetros, inicioTurno: u.inicioTurno ?? '07:00',
        fimTurno: u.fimTurno ?? '13:00', ehInstituicao: u.ehInstituicao,
        latitude: u.temCoordenadas ? u.latitude : null,
        longitude: u.temCoordenadas ? u.longitude : null
      });
    }
  }

  /** Consulta a localização do endereço digitado, sem salvar a unidade. */
  buscarLocalizacao(): void {
    const v = this.form.value;
    if (!v.endereco && !v.cidade) {
      this.erro.set('Informe ao menos o endereço ou a cidade para buscar a localização.');
      return;
    }

    this.buscandoLocal.set(true);
    this.erro.set('');
    this.previa.set(null);

    this.service.preverEndereco({
      nome: v.nome ?? undefined, endereco: v.endereco ?? undefined,
      numero: v.numero ?? undefined, bairro: v.bairro ?? undefined,
      cidade: v.cidade ?? undefined, uf: v.uf ?? undefined, cep: v.cep ?? undefined
    }).subscribe({
      next: (r) => { this.buscandoLocal.set(false); this.previa.set(r); },
      error: (err) => {
        this.buscandoLocal.set(false);
        this.erro.set(err?.error?.message ?? 'Não foi possível consultar a localização agora.');
      }
    });
  }

  /** Adota as coordenadas encontradas; a partir daí valem como definidas manualmente. */
  usarLocalizacao(): void {
    const p = this.previa();
    if (!p?.latitude || !p?.longitude) return;
    this.form.patchValue({ latitude: p.latitude, longitude: p.longitude });
    this.previa.set(null);
  }

  limparCoordenadas(): void {
    this.form.patchValue({ latitude: null, longitude: null });
  }

  onSubmit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.busy.set(true);
    this.erro.set('');

    const v = this.form.value;
    const dto = {
      nome: v.nome!,
      tipo: v.tipo || undefined,
      endereco: v.endereco || undefined,
      numero: v.numero || undefined,
      complemento: v.complemento || undefined,
      bairro: v.bairro || undefined,
      cidade: v.cidade || undefined,
      uf: v.uf || undefined,
      cep: v.cep || undefined,
      telefone: v.telefone || undefined,
      raioMetros: v.raioMetros ?? undefined,
      ehInstituicao: v.ehInstituicao ?? false,
      inicioTurno: v.inicioTurno || undefined,
      fimTurno: v.fimTurno || undefined
    };

    const operacao = this.editando
      ? this.service.update(this.data!.unidade!.id, dto)
      // Sem coordenadas informadas, a unidade entra na fila de geocodificação.
      : this.service.create({
          ...dto,
          latitude: v.latitude ?? undefined,
          longitude: v.longitude ?? undefined,
          geocodificarAgora: v.latitude == null
        });

    operacao.subscribe({
      next: (u) => { this.busy.set(false); this.dialogRef.close(u); },
      error: (err) => {
        this.busy.set(false);
        this.erro.set(err?.error?.message ?? 'Erro ao salvar a unidade.');
      }
    });
  }
}
