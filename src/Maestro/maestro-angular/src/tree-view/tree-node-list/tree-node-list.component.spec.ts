import { ComponentFixture, TestBed, waitForAsync } from '@angular/core/testing';

import { TreeNodeListComponent } from './tree-node-list.component';
import { NO_ERRORS_SCHEMA } from '@angular/core';

describe('TreeNodeListComponent', () => {
  let component: TreeNodeListComponent;
  let fixture: ComponentFixture<TreeNodeListComponent>;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [ TreeNodeListComponent ],
      schemas: [ NO_ERRORS_SCHEMA ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(TreeNodeListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
