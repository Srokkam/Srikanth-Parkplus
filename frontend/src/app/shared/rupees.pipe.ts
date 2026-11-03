import { Pipe, PipeTransform } from '@angular/core';
import { formatPaise } from '../core/money';

@Pipe({ name: 'rupees' })
export class RupeesPipe implements PipeTransform {
  transform(paise: number | null | undefined): string {
    return paise == null ? '' : formatPaise(paise);
  }
}
