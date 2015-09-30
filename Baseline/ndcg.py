import numpy as np
import math
from sys import *
def calc(lst):
    res = lst[0]
    for i in range(1, len(lst)):
        res += lst[i] / math.log(i+1, 2)
    return res

s = argv[1]
lineg=[l.rstrip().split(',') for l in 
open('run/CategoriesFeatures/categoryFeatures'+s+'TestAnonymized.csv', 
'r')]
linep=[l.rstrip().split(' ') for l in open('pred'+s+'.txt', 'r')]
gt = np.array(lineg)
pred = np.array(linep)
gt = gt[1:].transpose()[2:].transpose().astype(np.double)
pred = pred.astype(np.double)
avg=0
avgn=0
for i in range(1000):
    x=calc(list(zip(*sorted(zip(pred[i], gt[i]), reverse=True))[1]))
    y=calc(sorted(gt[i], reverse=True))
    if sum(gt[i].astype(np.bool)) >= 8:
        avgn += 1
        avg += x / y

print avg / avgn
