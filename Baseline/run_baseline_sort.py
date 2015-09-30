import numpy as np
import math
def calc(lst):
    res = lst[0]
    for i in range(1, len(lst)):
        res += lst[i] / math.log(i+1, 2)
    return res

tr = [l.rstrip().split(',') for l in open('run/CategoriesFeatures/categoryFeaturesSingaporeTrainAnonymized.csv','r')]
tr = np.array(tr)
tr = tr[1:].transpose()[2:].transpose().astype(np.double)
pred = tr
numt = tr.shape[1]
sumt = tr.sum(axis=0)
for i in range(numt):
	rnk = sorted(zip(tr[i,:], sumt, range(numt)), reverse=1)
	k = numt
	for j in rnk:
		pred[i][j[2]] = k
		k -= 1

lineg = [l.rstrip().split(',') for l in open('run/CategoriesFeatures/categoryFeaturesSingaporeTestAnonymized.csv','r')]
#linep = [l.rstrip().split(' ') for l in open('predLondon.txt', 'r')]
gt = np.array(lineg)
#pred = np.array(linep)
gt = gt[1:].transpose()[2:].transpose().astype(np.double)
pred = pred.astype(np.double)
avg = 0
avgn = 0
for i in range(len(gt)):
    x = calc(list(zip(*sorted(zip(pred[i], gt[i]), reverse=True))[1]))
    y = calc(sorted(gt[i], reverse=True))
    if sum(gt[i].astype(np.bool)) >= 8:
        avgn += 1
        avg += x / y

print avg / avgn