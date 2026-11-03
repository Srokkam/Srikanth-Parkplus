import { Pipe, PipeTransform } from '@angular/core';
import { IST, formatInstant } from '../core/time';

/** {{ order.createdUtc | localTime: zone }}. The one place instants are formatted for display. */
@Pipe({ name: 'localTime' })
export class LocalTimePipe implements PipeTransform {
  transform(utc: string | null | undefined, timeZone: string = IST): string {
    return utc ? formatInstant(utc, timeZone) : '';
  }
}
